#!/usr/bin/env python3
"""Install one isolated BusinessEntity on the fixed host; never bootstrap User Hub."""
import argparse
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import uuid
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit, parse_qs
from urllib.request import Request, build_opener, ProxyHandler, HTTPSHandler, HTTPRedirectHandler
import zipfile


class InstallError(RuntimeError):
    """Public error codes never include secret values or upstream response bodies."""


def require(condition, code):
    """Fail closed with a public diagnostic code."""
    if not condition:
        raise InstallError(code)


def digest(path):
    """Hash an artifact without loading the entire source archive in memory."""
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def run(*args, timeout=120, input=None):
    """Capture tool output privately; errors do not expose compose or API secrets."""
    result = subprocess.run(args, input=input, text=True, capture_output=True, timeout=timeout)
    require(result.returncode == 0, 'COMMAND_FAILED_' + Path(args[0]).name)
    return result.stdout


def atomic(path, value):
    """Persist private state atomically so retries preserve generated credentials."""
    path = Path(path)
    fd, temporary = tempfile.mkstemp(dir=path.parent, prefix=path.name + '.')
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as output:
            json.dump(value, output, ensure_ascii=False, indent=2)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def read_env(path):
    """Read the existing Hub installer format without executing shell statements."""
    values = {}
    for line in Path(path).read_text(encoding='utf-8-sig').splitlines():
        if line.strip() and not line.lstrip().startswith('#'):
            key, value = line.split('=', 1)
            values[key.strip()] = value.strip().strip("'\"")
    return values


class NoRedirect(HTTPRedirectHandler):
    """Do not follow redirects with management credentials or discovery requests."""
    def redirect_request(self, *args, **kwargs):
        return None


class Http:
    """HTTPS client using the existing installation CA and bounded responses."""
    def __init__(self, ca):
        self.opener = build_opener(ProxyHandler({}), NoRedirect(),
                                  HTTPSHandler(context=ssl.create_default_context(cafile=str(ca))))

    def request(self, url, method='GET', body=None, token=None, limit=1048576):
        """Return status/headers/body; callers explicitly validate non-200 responses."""
        headers = {'Accept': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        if body is not None:
            headers['Content-Type'] = 'application/json'
        request = Request(url, method=method, headers=headers,
                          data=json.dumps(body).encode() if body is not None else None)
        try:
            response = self.opener.open(request, timeout=5)
        except HTTPError as error:
            response = error
        with response:
            raw = response.read(limit + 1)
            require(len(raw) <= limit, 'HTTP_RESPONSE_TOO_LARGE')
            return response.status, response.headers, raw

    def json(self, url, **kwargs):
        """Only accept successful JSON responses; discard private upstream errors."""
        status, headers, raw = self.request(url, **kwargs)
        require(status in (200, 201) and headers.get_content_type() == 'application/json', 'HTTPS_JSON_FAILED')
        return json.loads(raw)


def validate_request(value):
    """Limit the first scenario to the explicitly selected host and isolated paths."""
    require(type(value.get('schemaVersion')) is int and value['schemaVersion'] == 1, 'REQUEST_SCHEMA')
    require(value['host'] == '192.168.55.15' and value['hubRoot'] == '/srv/user-hub', 'TARGET_MISMATCH')
    require(value['hubOrigin'] == 'https://192.168.55.15:9440', 'HUB_ORIGIN_MISMATCH')
    require(str(uuid.UUID(value['hubInstallationId'])) == value['hubInstallationId'], 'HUB_ID_INVALID')
    require(re.fullmatch(r'businessentity(?:-[a-z0-9]{1,20})?', value['installationName']), 'INSTALLATION_NAME')
    origin = urlsplit(value['applicationOrigin'])
    require(origin.scheme == 'https' and origin.hostname == value['host'] and origin.port == 7070
            and not origin.username and not origin.password and not origin.path and not origin.query and not origin.fragment,
            'APPLICATION_ORIGIN')
    login = value['adminLogin']
    require(isinstance(login, str) and 0 < len(login) <= 150 and login == login.strip()
            and not login.startswith('__') and not any(ord(c) < 32 for c in login), 'ADMIN_LOGIN_REQUIRED')
    return value


class Hub:
    """Inspect the existing Hub and use its management API only for owned BE objects."""
    def __init__(self, request):
        self.root = Path(request['hubRoot'])
        self.env = read_env(self.root / '.env')
        self.http = Http(self.env['TLS_CA_FILE'])
        self.passport = self.http.json(request['hubOrigin'] + '/.well-known/user-hub', limit=16384)
        p = self.passport
        require(type(p.get('schemaVersion')) is int and p['schemaVersion'] == 1
                and p.get('productId') == 'geo4.user-hub'
                and p.get('installationId') == request['hubInstallationId'], 'HUB_PASSPORT_MISMATCH')
        require(p['endpoints']['hub'] == request['hubOrigin'] == self.env['HUB_ORIGIN']
                and p['endpoints']['identity'] == self.env['AUTH_ORIGIN']
                and p['identityProvider']['productId'] == 'authentik' and 'oidc' in p['capabilities'], 'HUB_CONTRACT')
        descriptor = json.loads((self.root / 'runtime/installation.json').read_text())
        require(descriptor['installationId'] == p['installationId'], 'HUB_LOCAL_ID_MISMATCH')
        ready = self.http.json(request['hubOrigin'] + '/health/ready', limit=16384)
        require(ready.get('status') == 'ready', 'HUB_NOT_READY')
        self.token = self.env['BOT_API_TOKEN']
        require(self.env.get('BOOTSTRAP_COMPLETED') == 'true', 'HUB_BOOTSTRAP_INCOMPLETE')
        # The technical address comes only from the controlled local installation file.
        technical = urlsplit(self.env['TECH_ORIGIN'])
        require(technical.scheme == 'https' and technical.hostname == request['host']
                and not technical.username and not technical.path, 'MANAGEMENT_ORIGIN')

    def api(self, path, method='GET', body=None):
        """Call one existing management origin without exposing its token to BE."""
        return self.http.json(self.env['TECH_ORIGIN'] + '/api/v3/' + path,
                              method=method, body=body, token=self.token)

    def items(self, path):
        """Paginate on the fixed origin without following response-provided URLs."""
        result = []
        for page in range(1, 1001):
            batch = self.api(path + ('&' if '?' in path else '?') + f'page_size=100&page={page}')
            result.extend(batch['results'])
            if page >= batch['pagination']['total_pages']:
                return result
        raise InstallError('CATALOG_LIMIT')

    def administrator(self, login):
        """Resolve the chosen active human to the UUID used by user_uuid subjects."""
        users = [u for u in self.items('core/users/?username=' + quote(login, safe='')) if u['username'] == login]
        require(len(users) == 1 and users[0]['is_active'] and users[0].get('type') == 'internal', 'ADMIN_NOT_ACTIVE_HUMAN')
        user = users[0]
        user['uuid'] = str(uuid.UUID(user['uuid']))
        return user


def container_inventory():
    """Read only container IDs, ownership labels and published ports."""
    ids = run('docker', 'ps', '-aq').split()
    if not ids:
        return []
    return [json.loads(line) for line in run('docker', 'inspect', '--format', '{{json .}}', *ids).splitlines() if line]


def inspect(request, payload_hash):
    """Produce a non-secret reviewed plan; no identity objects or containers are created."""
    validate_request(request)
    hub = Hub(request)
    admin = hub.administrator(request['adminLogin'])
    root = Path('/srv') / request['installationName']
    inventory = container_inventory()
    hub_containers = [c for c in inventory if c['Config'].get('Labels', {}).get('io.geo4.product') == 'user-hub']
    require(any(c['Config']['Labels'].get('io.geo4.installation-id') == request['hubInstallationId'] for c in hub_containers), 'HUB_LABELS_MISSING')
    owned = [c for c in inventory if c['Config'].get('Labels', {}).get('com.docker.compose.project') == request['installationName']]
    state_file = root / 'installation.json'
    if root.exists():
        require(not root.is_symlink() and state_file.is_file(), 'DESTINATION_NOT_OWNED')
        state = json.loads(state_file.read_text())
        require(state.get('product') == 'businessentity' and state.get('request') == request, 'EXISTING_BINDING_MISMATCH')
        require(state.get('payloadSha256') == payload_hash, 'EXPLICIT_UPDATE_REQUIRED')
        require(all(c['Config'].get('Labels', {}).get('io.geo4.be-installation-id') == state['installationId'] for c in owned), 'CONTAINER_OWNER_MISMATCH')
    else:
        require(not owned, 'COMPOSE_NAME_IN_USE')
    port = urlsplit(request['applicationOrigin']).port
    for c in inventory:
        for bindings in c.get('HostConfig', {}).get('PortBindings', {}).values():
            if any(int(b['HostPort']) == port for b in bindings or []):
                require(c in owned, 'PORT_IN_USE')
    if not any(c['State']['Running'] for c in owned):
        with socket.socket() as probe:
            try:
                probe.bind(('0.0.0.0', port))
            except OSError:
                raise InstallError('PORT_IN_USE') from None
    # Certificate validity and SAN are checked before any deployment mutation.
    tls = hub.root / 'runtime/tls'
    for name in ('server.crt', 'server.key', 'ca.crt'):
        require((tls / name).is_file(), 'TLS_FILE_MISSING')
    run('openssl', 'verify', '-CAfile', str(tls / 'ca.crt'), '-verify_ip', request['host'], str(tls / 'server.crt'))
    run('docker', 'compose', 'version')
    plan = {'request': request, 'payloadSha256': payload_hash, 'administratorSubject': admin['uuid'],
            'hubInstallationId': hub.passport['installationId'], 'root': str(root)}
    approval = hashlib.sha256(json.dumps(plan, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return hub, admin, {'success': True, 'readOnly': True, 'plan': plan, 'approval': approval,
                        'hub': {k: hub.passport[k] for k in ('displayName', 'installationId', 'release')},
                        'applicationOrigin': request['applicationOrigin'], 'existing': root.exists()}


def extract_source(archive, destination):
    """Extract a verified source archive with no traversal, symlinks or duplicate paths."""
    with zipfile.ZipFile(archive) as source:
        names = set()
        require(sum(i.file_size for i in source.infolist()) <= 1024 * 1024 * 1024, 'SOURCE_TOO_LARGE')
        for item in source.infolist():
            path = Path(item.filename)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in item.filename
                    and item.filename not in names and (item.external_attr >> 16) & 0o170000 != 0o120000, 'SOURCE_PATH_INVALID')
            names.add(item.filename)
        source.extractall(destination)


def register_application(hub, state, state_path):
    """Create/reuse only this installation's OIDC client; do not edit Hub objects."""
    slug = 'be-' + state['installationId'].replace('-', '')
    request = state['request']
    callback = request['applicationOrigin'] + '/auth/callback/' + state['providerId'].replace('-', '') + '/1'
    templates = [p for p in hub.items('providers/oauth2/') if p['client_id'] == 'user-hub']
    require(len(templates) == 1, 'HUB_PROVIDER_AMBIGUOUS')
    template = templates[0]
    require(template['sub_mode'] == 'user_uuid' and template.get('signing_key'), 'HUB_PROVIDER_UNSUPPORTED')
    fields = {key: template[key] for key in ('authentication_flow', 'authorization_flow', 'invalidation_flow', 'signing_key', 'property_mappings')}
    fields.update(name=slug, client_id=slug, client_secret=state['clientSecret'], client_type='confidential',
                  grant_types=['authorization_code'], sub_mode='user_uuid', issuer_mode='per_provider',
                  redirect_uris=[{'matching_mode': 'strict', 'url': callback, 'redirect_uri_type': 'authorization'},
                                 {'matching_mode': 'strict', 'url': callback + '/signed-out', 'redirect_uri_type': 'logout'}])
    existing = [p for p in hub.items('providers/oauth2/') if p['client_id'] == slug]
    require(len(existing) <= 1, 'BE_PROVIDER_AMBIGUOUS')
    provider = existing[0] if existing else hub.api('providers/oauth2/', 'POST', fields)
    for key in ('name', 'client_id', 'client_secret', 'client_type', 'sub_mode', 'issuer_mode', 'redirect_uris'):
        require(provider.get(key) == fields[key], 'BE_PROVIDER_OWNERSHIP_OR_CONFIGURATION_MISMATCH')
    if state.get('authentikProviderId'):
        require(provider['pk'] == state['authentikProviderId'], 'BE_PROVIDER_ID_CHANGED')
    state['authentikProviderId'] = provider['pk']
    atomic(state_path, state)
    apps = [a for a in hub.items('core/applications/') if a['slug'] == slug]
    require(len(apps) <= 1, 'BE_APPLICATION_AMBIGUOUS')
    app = apps[0] if apps else hub.api('core/applications/', 'POST', {
        'name': 'BusinessEntity ' + request['installationName'], 'slug': slug,
        'provider': provider['pk'], 'policy_engine_mode': 'all', 'meta_launch_url': request['applicationOrigin']})
    require(app['provider'] == provider['pk'] and app['name'] == 'BusinessEntity ' + request['installationName'], 'BE_APPLICATION_OWNER_MISMATCH')
    issuer = hub.env['AUTH_ORIGIN'] + '/application/o/' + slug + '/'
    discovery = hub.http.json(issuer + '.well-known/openid-configuration')
    require(discovery['issuer'] == issuer, 'BE_ISSUER_MISMATCH')
    return slug, issuer


def compose_config(root, state, app_image, logger_image):
    """Use a private network and DB; expose only Kestrel HTTPS, without proxy headers."""
    labels = {'io.geo4.be-installation-id': state['installationId']}
    connection = 'Host=postgres-production-db;Port=5432;Database={};Username=businessentity;Password=' + state['dbPassword']
    common = {'restart': 'unless-stopped', 'labels': labels}
    return {'name': state['request']['installationName'], 'services': {
        'postgres-production-db': {**common, 'image': 'postgres:16-alpine',
            'environment': {'POSTGRES_USER': 'businessentity', 'POSTGRES_PASSWORD': state['dbPassword'], 'POSTGRES_DB': 'business_entity'},
            'volumes': ['pgdata:/var/lib/postgresql/data', str(root / 'init.sql') + ':/docker-entrypoint-initdb.d/01-logger.sql:ro'],
            'healthcheck': {'test': ['CMD-SHELL', 'pg_isready -U businessentity -d business_entity'], 'interval': '5s', 'timeout': '5s', 'retries': 30}},
        'web_logger': {**common, 'image': logger_image, 'environment': {'ASPNETCORE_URLS': 'http://+:80', 'ASPNETCORE_ENVIRONMENT': 'Production', 'IS_DOCKER': 'true',
            'ConnectionStrings__DockerConnection': connection.format('web_logger')}, 'depends_on': {'postgres-production-db': {'condition': 'service_healthy'}}},
        'business-entity': {**common, 'image': app_image, 'ports': ['7070:8443'],
            'environment': {'ASPNETCORE_URLS': 'https://+:8443', 'ASPNETCORE_ENVIRONMENT': 'Production', 'IS_DOCKER': 'true',
                # Explicit endpoints in appsettings override ASPNETCORE_URLS; configure TLS at that level.
                'Kestrel__Endpoints__Https__Url': 'https://+:8443',
                'Kestrel__Certificates__Default__Path': '/tls/server.crt', 'Kestrel__Certificates__Default__KeyPath': '/tls/server.key',
                'ConnectionStrings__DockerConnection': connection.format('business_entity'), 'IdentityConfigurationFile': '/app/identity.initial.json',
                'Identity__Secrets__primary': state['clientSecret'], 'Identity__KeyDirectory': '/app/identity-keys',
                'UserHub__Url': state['request']['hubOrigin'], 'Storage__RootPath': '/app/storage', 'Storage__HostRootPath': str(root / 'storage')},
            'volumes': [str(root / 'storage') + ':/app/storage', str(root / 'identity-keys') + ':/app/identity-keys',
                        str(root / 'identity.initial.json') + ':/app/identity.initial.json:ro',
                        str(root / 'tls') + ':/tls:ro'],
            'depends_on': {'postgres-production-db': {'condition': 'service_healthy'}}}},
        'volumes': {'pgdata': {}}, 'networks': {'default': {}}}


def verify(hub, state):
    """Verify real HTTPS and OIDC challenge; do not claim a completed browser login."""
    origin = state['request']['applicationOrigin']
    status, _, _ = hub.http.request(origin + '/', limit=1048576)
    require(status == 200, 'APPLICATION_HTTP_FAILED')
    status, headers, _ = hub.http.request(origin + '/auth/login')
    require(status == 302, 'OIDC_CHALLENGE_FAILED')
    target = urlsplit(headers['Location'])
    expected = urlsplit(hub.env['AUTH_ORIGIN'])
    require((target.scheme, target.netloc) == (expected.scheme, expected.netloc), 'OIDC_AUTHORIZATION_ORIGIN')
    query = parse_qs(target.query)
    callback = origin + '/auth/callback/' + state['providerId'].replace('-', '') + '/1'
    require(query.get('redirect_uri') == [callback] and query.get('client_id') == ['be-' + state['installationId'].replace('-', '')]
            and query.get('code_challenge_method') == ['S256'], 'OIDC_CHALLENGE_PARAMETERS')
    return {'https': True, 'oidcChallenge': True, 'browserLoginVerified': False}


def install(request, archive, approval):
    """First installation or identical retry; upgrades require a separate explicit path."""
    import fcntl
    with open('/run/lock/businessentity-install.lock', 'a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        hub, admin, report = inspect(request, digest(archive))
        require(approval == report['approval'], 'APPROVAL_PLAN_CHANGED')
        root = Path(report['plan']['root'])
        root.mkdir(mode=0o700, exist_ok=True)
        state_path = root / 'installation.json'
        if state_path.exists():
            state = json.loads(state_path.read_text())
        else:
            state = {'product': 'businessentity', 'installationId': str(uuid.uuid4()), 'providerId': str(uuid.uuid4()),
                     'request': request, 'payloadSha256': digest(archive), 'dbPassword': secrets.token_hex(32),
                     'clientSecret': secrets.token_hex(32), 'administratorSubject': admin['uuid'], 'phase': 'prepared'}
            atomic(state_path, state)
        require(state['administratorSubject'] == admin['uuid'], 'ADMIN_SUBJECT_CHANGED')
        if state['phase'] == 'installed':
            return {**report, 'readOnly': False, 'alreadyInstalled': True, 'checks': verify(hub, state)}
        try:
            # Freeze source and build before creating any external identity objects.
            source = root / 'source' / state['payloadSha256']
            if source.exists():
                require((source / '.complete').is_file(), 'SOURCE_INCOMPLETE')
            else:
                source.parent.mkdir(exist_ok=True)
                pending = Path(tempfile.mkdtemp(prefix='source-', dir=root))
                extract_source(archive, pending)
                (pending / '.complete').write_text(state['payloadSha256'])
                os.rename(pending, source)
            for directory in ('storage', 'identity-keys', 'tls'):
                (root / directory).mkdir(mode=0o700, exist_ok=True)
            for name in ('server.crt', 'server.key', 'ca.crt'):
                shutil.copyfile(hub.root / 'runtime/tls' / name, root / 'tls' / name)
                os.chmod(root / 'tls' / name, 0o600 if name.endswith('.key') else 0o644)
            tag = state['payloadSha256'][:24]
            app_image = request['installationName'] + '-app:' + tag
            logger_image = request['installationName'] + '-logger:' + tag
            run('docker', 'build', '-t', app_image + '-base', '-f', str(source / 'BusinessEntity/Dockerfile'), str(source), timeout=2400)
            run('docker', 'build', '-t', logger_image, '-f', str(source / 'BlazorServerWebLogger/Dockerfile'), str(source), timeout=2400)
            # Add the host's CA to the app image. HTTPS validation remains enabled.
            ca_build = root / 'ca-build'
            ca_build.mkdir(exist_ok=True)
            shutil.copyfile(root / 'tls/ca.crt', ca_build / 'ca.crt')
            tls_dockerfile = f'FROM {app_image}-base\nUSER root\nCOPY ca.crt /usr/local/share/ca-certificates/userhub.crt\nRUN update-ca-certificates\n'
            (ca_build / 'Dockerfile').write_text(tls_dockerfile)
            run('docker', 'build', '-t', app_image, str(ca_build), timeout=300)
            slug, issuer = register_application(hub, state, state_path)
            profile = {'Id': state['providerId'], 'Name': hub.passport['displayName'], 'Revision': 1, 'Enabled': True,
                       'Issuer': issuer, 'MetadataAddress': issuer + '.well-known/openid-configuration', 'ClientId': slug,
                       'ClientSecretRef': 'primary', 'Scopes': ['openid', 'profile', 'email'], 'SubjectMode': 'user_uuid',
                       'ProvisioningKind': 'None', 'ManagedLocal': False, 'ApiTokenProfiles': []}
            atomic(root / 'identity.initial.json', {'Identity': {'InitialProvider': profile, 'Bootstrap': {
                'AdministratorIssuer': issuer, 'AdministratorSubject': state['administratorSubject']}}})
            (root / 'init.sql').write_text('CREATE DATABASE web_logger;\n')
            atomic(root / 'compose.json', compose_config(root, state, app_image, logger_image))
            args = ('docker', 'compose', '-p', request['installationName'], '-f', str(root / 'compose.json'))
            run(*args, 'config', '--quiet')
            state['phase'] = 'starting'
            atomic(state_path, state)
            run(*args, 'up', '-d', timeout=300)
            deadline = time.monotonic() + 240
            while True:
                try:
                    checks = verify(hub, state)
                    break
                except Exception:
                    if time.monotonic() >= deadline:
                        raise InstallError('APPLICATION_READINESS_TIMEOUT') from None
                    time.sleep(3)
            state['phase'] = 'installed'
            atomic(state_path, state)
            return {**report, 'readOnly': False, 'checks': checks, 'installationId': state['installationId']}
        except Exception:
            state['phase'] = 'incomplete'
            atomic(state_path, state)
            # Retain owned data and registration for diagnosis and safe identical retry.
            raise


def main():
    """The SSH wrapper supplies verified private files; stdout contains one public report."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['inspect', 'install'])
    parser.add_argument('--request', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--approval', default='')
    args = parser.parse_args()
    try:
        request = validate_request(json.loads(args.request.read_text(encoding='utf-8-sig')))
        result = inspect(request, digest(args.source))[2] if args.command == 'inspect' else install(request, args.source, args.approval)
        print(json.dumps(result, ensure_ascii=False))
    except Exception as error:
        print(json.dumps({'success': False, 'error': str(error) if isinstance(error, InstallError) else type(error).__name__}))
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())

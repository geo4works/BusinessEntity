#!/usr/bin/env python3
"""Generate pinned BotFactory definitions after rebuilding the local payload."""
import hashlib
import json
import re
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
ARTIFACTS = ROOT / 'artifacts/botfactory-businessentity'
CLIENT_ALIAS = '4f38535d-faaa-4904-a658-44dd1565c85d'
WINDOWS_ARTIFACTS = r'C:\Develop\BusinessEntityProduct\BusinessEntity\artifacts\botfactory-businessentity'
LOCAL_PATH = r"""function Local-Path([string]$p){if(-not $IsWindows -and $p -match '^([A-Za-z]):[\\/](.*)$'){return '/mnt/'+$Matches[1].ToLowerInvariant()+'/'+$Matches[2].Replace('\','/')};return $p}"""


def step(alias, order, name, prompt='', model='PWSH', reference=None):
    """Use deterministic IDs within this scenario family, independent of other scenarios."""
    return {'id': str(uuid.uuid5(uuid.NAMESPACE_URL, alias + '/step/' + str(order))),
            'scenarioId': str(uuid.uuid5(uuid.NAMESPACE_URL, alias)), 'name': name, 'order': order,
            'isActive': True, 'modelAlias': 'not-specified', 'modelSet': model,
            'prompt': prompt, 'scenarioRefAlias': reference, 'isScenarioRefSelected': reference is not None,
            'isModelSectionVisible': reference is None, 'checkSuccess': False, 'postToMessenger': False}


def generate():
    """Refresh local definitions; publishing to Warehouse remains a separate versioned operation."""
    manifest_hash = hashlib.sha256((ARTIFACTS / 'manifest.json').read_bytes()).hexdigest().upper()
    project = json.loads((HERE / 'client-scenario.xlitecodeproj').read_text())
    body = (HERE / 'vpn-prefix.ps1').read_text() + '\n' + (HERE / 'client-runner.ps1').read_text().replace('__MANIFEST_SHA256__', manifest_hash)
    for pattern in project['scriptExecutionPatterns']:
        pattern['scriptBody'] = body
    (HERE / 'client-scenario.xlitecodeproj').write_text(json.dumps(project, ensure_ascii=False, indent=2) + '\n')
    for mode in ('inspect', 'install'):
        alias = 'BUSINESSENTITY-' + mode.upper() + '-USERHUB-V1'
        preparation = f"""# _{{{{remote-command}}}} _{{{{remote-content}}}} _{{{{remote-timeout-minutes}}}} _{{{{remote-out-subfolder}}}}
$ErrorActionPreference='Stop'
{LOCAL_PATH}
$source=Local-Path '{WINDOWS_ARTIFACTS}'
$payload=Join-Path '{{TmpFolder}}' 'businessentity-input'
New-Item -ItemType Directory -Force $payload|Out-Null
if((Get-FileHash (Join-Path $source 'manifest.json') -Algorithm SHA256).Hash -ne '{manifest_hash}'){{throw 'Pinned BusinessEntity payload changed; rebuild and republish scenario'}}
$manifest=Get-Content (Join-Path $source 'manifest.json') -Raw|ConvertFrom-Json
Copy-Item (Join-Path $source 'manifest.json') $payload
foreach($entry in $manifest.files.PSObject.Properties){{
 if($entry.Name -notin @('source.zip','host_install.py','request.json')){{throw 'Unexpected payload'}}
 if((Get-FileHash (Join-Path $source $entry.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value){{throw 'Payload hash mismatch'}}
 Copy-Item (Join-Path $source $entry.Name) $payload
}}
"""
        if mode == 'install':
            preparation += """$plan=Get-Content (Join-Path $source 'preflight.json') -Raw|ConvertFrom-Json
if(-not $plan.success -or $plan.approval -notmatch '^[a-f0-9]{64}$' -or $plan.plan.payloadSha256 -ne $manifest.files.'source.zip'){throw 'Run BusinessEntity preflight and review its result first'}
Copy-Item (Join-Path $source 'preflight.json') (Join-Path $payload 'approved-plan.json')
"""
        preparation += f"""Write-Output '<<<BOTMGR_VARS_PACK_V1'
Write-Output (@{{locals=@{{vars=@(@{{name='remote-command';value='BUSINESSENTITY-{mode.upper()};'}},@{{name='remote-content';value=$payload}},@{{name='remote-timeout-minutes';value='115'}},@{{name='remote-out-subfolder';value='businessentity-{mode}'}})}}}}|ConvertTo-Json -Depth 8 -Compress)
Write-Output 'BOTMGR_VARS_PACK_V1>>>'
"""
        result_name = 'preflight.json' if mode == 'inspect' else 'installation-result.json'
        final = f"""# _{{{{{{say}}}}}}
$ErrorActionPreference='Stop'
{LOCAL_PATH}
$source=Join-Path '{{TmpFolder}}' 'rcmd/businessentity-{mode}/result.json'
$target=Local-Path '{WINDOWS_ARTIFACTS}'
Copy-Item $source (Join-Path $target '{result_name}') -Force
$result=Get-Content $source -Raw|ConvertFrom-Json
if(-not $result.success){{throw ('BusinessEntity failed: '+$result.error+' '+$result.failedPhase)}}
"""
        if mode == 'inspect':
            final += "$say='Найден '+$result.hub.displayName+' '+$result.hub.release.version+' ('+$result.hub.installationId+'). Проверены Hub, admin и HTTPS7070. Для подключения к этому Hub выберите BusinessEntity — установить на 7070 в существующий User Hub. Отдельный Hub в этом сценарии пока не устанавливается.'\n"
        else:
            final += "$say='BusinessEntity развёрнут: '+$result.applicationOrigin+'. HTTPS и OIDC-перенаправление проверены. Первый администратор: admin. Требуется завершить настоящий браузерный вход; эта проверка не выполняется установщиком автоматически.'\n"
        final += """Write-Output '<<<BOTMGR_VARS_PACK_V1'
Write-Output (@{globals=@{vars=@(@{name='say';value=$say})}}|ConvertTo-Json -Depth 8 -Compress)
Write-Output 'BOTMGR_VARS_PACK_V1>>>'
"""
        scenario = {'id': str(uuid.uuid5(uuid.NAMESPACE_URL, alias)), 'alias': alias,
                    'name': 'BusinessEntity — ' + ('проверить Hub и установку на 7070' if mode == 'inspect' else 'установить на 7070 в существующий User Hub'),
                    'isActive': True, 'isPublished': True, 'order': 0, 'parent': '00000000-0000-0000-0000-000000000000',
                    'scenarioType': 0, 'runMode': 'Singleton', 'requiredClientScenarioAliasGuid': CLIENT_ALIAS,
                    'needRepository': False, 'usePresets': False, 'useUserInput': False, 'userPrompt': '',
                    'postStepsToMessenger': False, 'deleteIntermediateMessages': False, 'solid': False, 'exportHead': False,
                    'customAgentScenarioSteps': [step(alias, 1, 'Подготовить закреплённый пакет', preparation),
                        step(alias, 2, 'Выполнить через client001', model='empty', reference='GEO-RCMD_D9DQN'),
                        step(alias, 3, 'Сохранить результат', final),
                        step(alias, 4, 'Показать результат', '[custom-workflow:simple-say-workflow; say={{{say}}}]', model='workflow')]}
        (HERE / (mode + '-server-scenario.json')).write_text(json.dumps(scenario, ensure_ascii=False, indent=2) + '\n')
    for name in ('status-client-scenario.xlitecodeproj', 'status-server-scenario.json'):
        path = HERE / name
        if path.exists():
            text = re.sub(r"(?<=-ne ')[A-F0-9]{64}(?=')", manifest_hash, path.read_text())
            path.write_text(text)
    print('Client and server scenario definitions generated; publish with fresh Warehouse versions.')


if __name__ == '__main__':
    generate()

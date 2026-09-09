import { Editor, Node } from "@tiptap/core";
import StarterKit from "@tiptap/starter-kit";
import Heading from "@tiptap/extension-heading";
import Paragraph from "@tiptap/extension-paragraph";
import Table from "@tiptap/extension-table";
import TableCell from "@tiptap/extension-table-cell";
import TableHeader from "@tiptap/extension-table-header";
import TableRow from "@tiptap/extension-table-row";
import Underline from "@tiptap/extension-underline";
import { TextSelection } from "@tiptap/pm/state";

const CustomHeading = Heading.extend({
    addAttributes() {
        return {
            ...(this.parent?.() ?? {}),
            id: {
                default: null,
                parseHTML: element => element.getAttribute("id"),
                renderHTML: attributes => attributes.id ? { id: attributes.id } : {}
            },
            "data-chunk-id": {
                default: null,
                parseHTML: element => element.getAttribute("data-chunk-id"),
                renderHTML: attributes => attributes["data-chunk-id"] ? { "data-chunk-id": attributes["data-chunk-id"] } : {}
            },
            "data-block-index": {
                default: null,
                parseHTML: element => element.getAttribute("data-block-index"),
                renderHTML: attributes => attributes["data-block-index"] ? { "data-block-index": attributes["data-block-index"] } : {}
            }
        };
    }
});

const RichTextImage = Node.create({
    name: "richTextImage",
    inline: true,
    group: "inline",
    atom: true,
    selectable: true,
    draggable: true,

    addAttributes() {
        return {
            src: { default: null },
            imageId: { default: null },
            displayVariant: { default: "original" },
            altText: { default: "" },
            width: { default: null },
            height: { default: null }
        };
    },

    parseHTML() {
        return [
            {
                tag: "span.rich-text-inline-image",
                getAttrs: element => readImageAttributes(element)
            },
            {
                tag: "img[data-rich-image-id]",
                getAttrs: element => readImageAttributes(element)
            },
            {
                tag: "img[src*='/rich-document-files/']",
                getAttrs: element => readImageAttributes(element)
            }
        ];
    },

    renderHTML({ node }) {
        const spanAttrs = buildInlineImageSpanAttributes(node.attrs);
        const imgAttrs = buildImageDomAttributes(node.attrs);
        return ["span", spanAttrs, ["img", imgAttrs]];
    }
});

const RichTextVideo = Node.create({
    name: "richTextVideo",
    inline: true,
    group: "inline",
    atom: true,
    selectable: true,
    draggable: true,

    addAttributes() {
        return {
            src: { default: null },
            videoId: { default: null },
            title: { default: "" },
            uploadToken: { default: null },
            isUploading: { default: false },
            uploadError: { default: "" }
        };
    },

    parseHTML() {
        return [
            {
                tag: "span.rich-text-inline-video",
                getAttrs: element => readVideoAttributes(element)
            },
            {
                tag: "video[data-rich-video-id]",
                getAttrs: element => readVideoAttributes(element)
            },
            {
                tag: "video[src*='/media-server-files/videos/']",
                getAttrs: element => readVideoAttributes(element)
            }
        ];
    },

    renderHTML({ node }) {
        const spanAttrs = buildInlineVideoSpanAttributes(node.attrs);
        if (node.attrs.isUploading) {
            return [
                "span",
                spanAttrs,
                [
                    "span",
                    {
                        class: node.attrs.uploadError
                            ? "rich-text-inline-video-upload-placeholder rich-text-inline-video-upload-placeholder--failed"
                            : "rich-text-inline-video-upload-placeholder",
                        title: node.attrs.uploadError || node.attrs.title || "Видео загружается"
                    },
                    ["span", { class: "rich-text-inline-video-upload-spinner", "aria-hidden": "true" }],
                    ["span", { class: "rich-text-inline-video-upload-play", "aria-hidden": "true" }]
                ]
            ];
        }

        const videoAttrs = buildVideoDomAttributes(node.attrs);
        return ["span", spanAttrs, ["video", videoAttrs]];
    }
});

const RichTextTable = Table.extend({
    addAttributes() {
        return {
            ...(this.parent?.() ?? {}),
            rowNumbers: {
                default: false,
                parseHTML: element => element.getAttribute("data-rich-table-row-numbers") === "true",
                renderHTML: attributes => attributes.rowNumbers ? { "data-rich-table-row-numbers": "true" } : {}
            }
        };
    }
});

const RichTextTableCell = TableCell.extend({
    addAttributes() {
        return {
            ...(this.parent?.() ?? {}),
            colwidth: {
                default: null,
                parseHTML: element => parseColumnWidths(
                    element.getAttribute("data-colwidth") ||
                    element.getAttribute("colwidth") ||
                    ""),
                renderHTML: attributes => renderColumnWidths(attributes)
            },
            rowNumber: {
                default: false,
                parseHTML: element => element.getAttribute("data-rich-table-row-number") === "true",
                renderHTML: attributes => attributes.rowNumber ? { "data-rich-table-row-number": "true" } : {}
            },
            cellBackground: {
                default: null,
                parseHTML: element => readCellBackground(element),
                renderHTML: attributes => renderCellBackground(attributes)
            }
        };
    }
});

const RichTextTableHeader = TableHeader.extend({
    addAttributes() {
        return {
            ...(this.parent?.() ?? {}),
            colwidth: {
                default: null,
                parseHTML: element => parseColumnWidths(
                    element.getAttribute("data-colwidth") ||
                    element.getAttribute("colwidth") ||
                    ""),
                renderHTML: attributes => renderColumnWidths(attributes)
            },
            rowNumber: {
                default: false,
                parseHTML: element => element.getAttribute("data-rich-table-row-number") === "true",
                renderHTML: attributes => attributes.rowNumber ? { "data-rich-table-row-number": "true" } : {}
            },
            cellBackground: {
                default: null,
                parseHTML: element => readCellBackground(element),
                renderHTML: attributes => renderCellBackground(attributes)
            }
        };
    }
});

const extensions = [
    StarterKit.configure({
        heading: false,
        paragraph: false
    }),
    Paragraph,
    CustomHeading.configure({ levels: [1, 2, 3] }),
    RichTextImage,
    RichTextVideo,
    RichTextTable.configure({
        resizable: true,
        handleWidth: 7,
        cellMinWidth: 40,
        lastColumnResizable: false,
        allowTableNodeSelection: true
    }),
    TableRow,
    RichTextTableHeader,
    RichTextTableCell,
    Underline
];

const registries = new Map();
const editorDebugContexts = new WeakMap();
let activeImageMenu = null;
let activeCellBackgroundPalette = null;

const cellBackgroundPalette = [
    { label: "Без заливки", color: null },
    { label: "Серый", color: "#f1f3f5" },
    { label: "Желтый", color: "#fff3bf" },
    { label: "Красный", color: "#ffe3e3" },
    { label: "Зеленый", color: "#d3f9d8" },
    { label: "Синий", color: "#d0ebff" },
    { label: "Фиолетовый", color: "#e5dbff" },
    { label: "Розовый", color: "#ffdeeb" },
    { label: "Оранжевый", color: "#ffe8cc" }
];

function getRegistry(viewportElementId) {
    let registry = registries.get(viewportElementId);
    if (!registry) {
        registry = {
            editors: new Map(),
            activeSortOrder: null,
            dotNetReference: null,
            documentId: null,
            toolbarGuardElement: null
        };
        registries.set(viewportElementId, registry);
    }

    return registry;
}

function getEditorToolbar(viewportElementId) {
    const viewport = document.getElementById(viewportElementId);
    return viewport
        ?.closest(".rich-text-document-editor")
        ?.querySelector(".rich-text-document-editor__toolbar") ?? null;
}

function ensureToolbarGuard(viewportElementId, registry) {
    const toolbar = getEditorToolbar(viewportElementId);
    if (!toolbar || registry.toolbarGuardElement === toolbar) {
        return;
    }

    toolbar.addEventListener("mousedown", event => {
        const button = event.target?.closest?.("[data-rich-text-editor-command]");
        if (!button || !toolbar.contains(button)) {
            return;
        }

        // Keep the ProseMirror selection alive while a toolbar button is clicked.
        event.preventDefault();
    });

    registry.toolbarGuardElement = toolbar;
}

function setToolbarToggleState(toolbar, command, active) {
    const button = toolbar.querySelector(`[data-rich-text-editor-command="${command}"]`);
    if (!button) {
        return;
    }

    const normalizedActive = active === true;
    button.classList.toggle("active", normalizedActive);
    button.classList.toggle("rich-text-document-editor__toggle-button--active", normalizedActive);
    button.setAttribute("aria-pressed", normalizedActive ? "true" : "false");
}

function setToolbarTableControlsVisible(toolbar, visible) {
    const normalizedVisible = visible === true;
    toolbar
        .querySelectorAll("[data-rich-text-editor-table-control=\"true\"]")
        .forEach(control => {
            control.classList.toggle("rich-text-document-editor__table-control--hidden", !normalizedVisible);
            control.toggleAttribute("hidden", !normalizedVisible);
            control.setAttribute("aria-hidden", normalizedVisible ? "false" : "true");
        });
}

function setToolbarCellBackgroundState(toolbar, editor) {
    const button = toolbar.querySelector("[data-rich-text-editor-command=\"openCellBackgroundPalette\"]");
    if (!button) {
        return;
    }

    const color = getCurrentCellBackground(editor);
    const swatch = button.querySelector("[data-rich-text-cell-background-swatch]");
    if (swatch) {
        swatch.style.background = color ?? "";
        swatch.classList.toggle("rich-text-document-editor__cell-fill-swatch--empty", !color);
    }

    button.classList.toggle("rich-text-document-editor__toggle-button--active", !!color);
}

function syncToolbarState(viewportElementId, registry, editor) {
    ensureToolbarGuard(viewportElementId, registry);

    const toolbar = getEditorToolbar(viewportElementId);
    if (!toolbar) {
        return;
    }

    const tableInfo = editor ? findSelectedTable(editor) : null;
    const tableHeaderType = editor?.schema?.nodes?.tableHeader ?? null;
    setToolbarTableControlsVisible(toolbar, !!tableInfo);
    setToolbarToggleState(
        toolbar,
        "toggleTableHeaderRow",
        tableInfo && tableHeaderType && isFirstTableRowHeader(tableInfo.node, tableHeaderType));
    setToolbarToggleState(
        toolbar,
        "toggleTableRowNumbers",
        tableInfo && isTableRowNumberingEnabled(tableInfo.node));
    setToolbarCellBackgroundState(toolbar, editor);
}

function toSortOrder(value) {
    const sortOrder = Number(value);
    return Number.isFinite(sortOrder) ? sortOrder : -1;
}

function toPositiveInt(value) {
    const number = Number(value);
    return Number.isFinite(number) && number > 0
        ? Math.round(number)
        : null;
}

function parseColumnWidths(value) {
    if (typeof value !== "string" || value.trim().length === 0) {
        return null;
    }

    const widths = value
        .split(",")
        .map(part => toPositiveInt(part))
        .filter(width => width != null);

    return widths.length > 0 ? widths : null;
}

function normalizeColumnWidths(value) {
    if (!Array.isArray(value)) {
        return null;
    }

    const widths = value
        .map(width => toPositiveInt(width))
        .filter(width => width != null);

    return widths.length > 0 ? widths : null;
}

function renderColumnWidths(attributes) {
    const widths = normalizeColumnWidths(attributes?.colwidth);
    return widths ? { "data-colwidth": widths.join(",") } : {};
}

function normalizeCellBackground(value) {
    if (value == null) {
        return null;
    }

    const text = String(value).trim();
    if (!text || text.toLowerCase() === "transparent" || text.toLowerCase() === "none") {
        return null;
    }

    const match = text.match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
    if (!match) {
        return null;
    }

    const hex = match[1].toLowerCase();
    return hex.length === 3
        ? `#${hex[0]}${hex[0]}${hex[1]}${hex[1]}${hex[2]}${hex[2]}`
        : `#${hex}`;
}

function readCssDeclaration(styleText, propertyName) {
    if (typeof styleText !== "string" || styleText.trim().length === 0) {
        return "";
    }

    const normalizedPropertyName = propertyName.toLowerCase();
    for (const declaration of styleText.split(";")) {
        const separatorIndex = declaration.indexOf(":");
        if (separatorIndex < 0) {
            continue;
        }

        const name = declaration.slice(0, separatorIndex).trim().toLowerCase();
        if (name !== normalizedPropertyName) {
            continue;
        }

        return declaration.slice(separatorIndex + 1).trim();
    }

    return "";
}

function readCellBackground(element) {
    if (!element) {
        return null;
    }

    return normalizeCellBackground(element.getAttribute("data-rich-cell-background")) ??
        normalizeCellBackground(readCssDeclaration(element.getAttribute("style") ?? "", "background-color")) ??
        normalizeCellBackground(readCssDeclaration(element.getAttribute("style") ?? "", "background")) ??
        normalizeCellBackground(element.getAttribute("bgcolor"));
}

function renderCellBackground(attributes) {
    const color = normalizeCellBackground(attributes?.cellBackground);
    return color
        ? {
            "data-rich-cell-background": color,
            style: `background-color: ${color};`
        }
        : {};
}

function isTableDebugEnabled() {
    try {
        if (window.localStorage?.getItem("richDocTableDebug") === "0") {
            return false;
        }

        return window.richDocTableDebug === true || window.localStorage?.getItem("richDocTableDebug") === "1";
    } catch {
        return window.richDocTableDebug === true;
    }
}

function isTableDebugWebLoggerEnabled() {
    try {
        return window.localStorage?.getItem("richDocTableDebug") !== "0";
    } catch {
        return true;
    }
}

function roundMetric(value) {
    return Number.isFinite(value) ? Math.round(value * 10) / 10 : null;
}

function readPixelWidth(value) {
    const match = String(value ?? "").match(/([0-9]+(?:\.[0-9]+)?)/);
    if (!match) {
        return null;
    }

    return toPositiveInt(match[1]);
}

function readRenderedTableColumnWidths(tableElement) {
    if (!tableElement) {
        return [];
    }

    const colgroup = Array.from(tableElement.children)
        .find(child => child.tagName?.toLowerCase() === "colgroup");
    const columns = colgroup
        ? Array.from(colgroup.children).filter(child => child.tagName?.toLowerCase() === "col")
        : [];
    if (columns.length === 0) {
        return [];
    }

    const widths = columns.map(column => readPixelWidth(column.style?.width || column.getAttribute("width")));
    return widths.some(width => width != null) ? widths : [];
}

function getTableDebugSnapshot(editor) {
    const editorDom = editor?.view?.dom;
    if (!editorDom) {
        return [];
    }

    return Array.from(editorDom.querySelectorAll("table")).map((table, tableIndex) => {
        const tableRect = table.getBoundingClientRect();
        const colgroup = Array.from(table.children)
            .find(child => child.tagName?.toLowerCase() === "colgroup");
        const columns = colgroup
            ? Array.from(colgroup.children).filter(child => child.tagName?.toLowerCase() === "col")
            : [];
        const firstRow = table.rows?.[0] ?? null;

        return {
            tableIndex,
            tableHeight: roundMetric(tableRect.height),
            tableWidth: roundMetric(tableRect.width),
            handleCount: table.querySelectorAll(".column-resize-handle").length,
            rowHeights: Array.from(table.rows ?? []).map(row => roundMetric(row.getBoundingClientRect().height)),
            columnStyles: columns.map(column => column.style?.width || column.getAttribute("width") || ""),
            firstRowCellWidths: firstRow
                ? Array.from(firstRow.cells).map(cell => ({
                    tag: cell.tagName.toLowerCase(),
                    width: roundMetric(cell.getBoundingClientRect().width),
                    dataColwidth: cell.getAttribute("data-colwidth") || "",
                    colwidth: cell.getAttribute("colwidth") || ""
                }))
                : []
        };
    });
}

function debugTableMetrics(source, editor, extra = {}) {
    const writeToConsole = isTableDebugEnabled();
    const writeToWebLogger = isTableDebugWebLoggerEnabled();
    if (!writeToConsole && !writeToWebLogger) {
        return;
    }

    const payload = {
        source,
        ...extra,
        tables: getTableDebugSnapshot(editor)
    };

    if (writeToConsole) {
        console.debug("[rich-doc-table]", source, payload);
    }

    const context = editorDebugContexts.get(editor);
    const dotNetReference = context?.registry?.dotNetReference;
    if (!writeToWebLogger || !dotNetReference) {
        return;
    }

    dotNetReference
        .invokeMethodAsync(
            "OnTableDebug",
            JSON.stringify({
                sortOrder: context.sortOrder,
                ...payload
            }))
        .catch(() => {});
}

function normalizeTableResizeHandles(root) {
    if (!root) {
        return;
    }

    root.querySelectorAll?.(".column-resize-handle").forEach(handle => {
        handle.style.setProperty("display", "none", "important");
        handle.style.setProperty("position", "fixed", "important");
        handle.style.setProperty("left", "-10000px", "important");
        handle.style.setProperty("top", "-10000px", "important");
        handle.style.setProperty("width", "0", "important");
        handle.style.setProperty("height", "0", "important");
        handle.style.setProperty("min-width", "0", "important");
        handle.style.setProperty("min-height", "0", "important");
        handle.style.setProperty("margin", "0", "important");
        handle.style.setProperty("padding", "0", "important");
        handle.style.setProperty("border", "0", "important");
        handle.style.setProperty("overflow", "hidden", "important");
        handle.style.setProperty("line-height", "0", "important");
        handle.style.setProperty("font-size", "0", "important");
        handle.style.setProperty("contain", "strict", "important");
        handle.setAttribute("aria-hidden", "true");
    });
}

function attachTableResizeHandleGuard(host) {
    normalizeTableResizeHandles(host);

    const observer = new MutationObserver(mutations => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node instanceof Element) {
                    if (node.classList.contains("column-resize-handle") || node.querySelector?.(".column-resize-handle")) {
                        normalizeTableResizeHandles(host);
                        return;
                    }
                }
            }
        }
    });

    observer.observe(host, {
        childList: true,
        subtree: true
    });

    return () => observer.disconnect();
}

function attachTableDebugging(editor, host, registry, sortOrder) {
    let lastLoggedAt = 0;
    editorDebugContexts.set(editor, { registry, sortOrder });

    const onMouseMove = event => {
        if (!isTableDebugEnabled()) {
            return;
        }

        const target = event.target instanceof Element ? event.target : null;
        const cell = target?.closest?.("td, th");
        normalizeTableResizeHandles(host);
        if (!cell || !host.contains(cell)) {
            return;
        }

        const rect = cell.getBoundingClientRect();
        const isNearEdge = event.clientX - rect.left <= 8 || rect.right - event.clientX <= 8;
        const hasHandle = host.querySelector(".column-resize-handle") != null;
        if (!isNearEdge && !hasHandle) {
            return;
        }

        const now = Date.now();
        if (now - lastLoggedAt < 900) {
            return;
        }

        lastLoggedAt = now;
        debugTableMetrics("hover", editor, {
            sortOrder,
            nearEdge: isNearEdge,
            targetTag: cell.tagName.toLowerCase(),
            targetRect: {
                height: roundMetric(rect.height),
                width: roundMetric(rect.width)
            }
        });
    };

    host.addEventListener("mousemove", onMouseMove, true);
    return () => {
        host.removeEventListener("mousemove", onMouseMove, true);
        editorDebugContexts.delete(editor);
    };
}

function applyColumnWidthsToSerializedTable(tableElement, columnWidths) {
    if (!tableElement || !Array.isArray(columnWidths) || columnWidths.length === 0) {
        return;
    }

    const rows = Array.from(tableElement.querySelectorAll("tr"))
        .filter(row => row.closest("table") === tableElement);

    for (const row of rows) {
        let columnIndex = 0;
        const cells = Array.from(row.children)
            .filter(cell => cell.matches?.("td, th"));

        for (const cell of cells) {
            const colspan = toPositiveInt(cell.getAttribute("colspan")) || 1;
            const cellWidths = columnWidths
                .slice(columnIndex, columnIndex + colspan)
                .filter(width => width != null);

            if (cellWidths.length > 0) {
                cell.setAttribute("data-colwidth", cellWidths.join(","));
            }

            columnIndex += colspan;
        }
    }
}

function serializeEditorHtml(editor) {
    const html = editor?.getHTML?.() ?? "";
    const editorDom = editor?.view?.dom;
    if (!editorDom || !html.includes("<table")) {
        return html;
    }

    const renderedTables = Array.from(editorDom.querySelectorAll("table"));
    if (renderedTables.length === 0) {
        return html;
    }

    const template = document.createElement("template");
    template.innerHTML = html;
    const serializedTables = Array.from(template.content.querySelectorAll("table"));
    if (serializedTables.length === 0) {
        return html;
    }

    serializedTables.forEach((serializedTable, index) => {
        const columnWidths = readRenderedTableColumnWidths(renderedTables[index]);
        applyColumnWidthsToSerializedTable(serializedTable, columnWidths);
    });

    debugTableMetrics("serialize", editor, {
        serializedTableCount: serializedTables.length
    });

    return template.innerHTML;
}

function readImageAttributes(element) {
    if (!element) {
        return false;
    }

    const isImage = element.matches?.("img") === true;
    const imageElement = isImage ? element : element.querySelector?.("img");
    const src = imageElement?.getAttribute("src") || element.getAttribute("src") || "";
    const parsed = parseRichDocumentImageUrl(src);
    const imageId =
        element.getAttribute("data-rich-image-id") ||
        imageElement?.getAttribute("data-rich-image-id") ||
        parsed.imageId;
    if (!imageId) {
        return false;
    }

    return {
        src,
        imageId,
        displayVariant:
            element.getAttribute("data-display-variant") ||
            imageElement?.getAttribute("data-display-variant") ||
            parsed.variant ||
            "original",
        altText:
            element.getAttribute("data-alt-text") ||
            imageElement?.getAttribute("alt") ||
            element.getAttribute("alt") ||
            "",
        width:
            toPositiveInt(element.getAttribute("data-width")) ||
            toPositiveInt(element.getAttribute("width")) ||
            toPositiveInt(imageElement?.getAttribute("width")),
        height:
            toPositiveInt(element.getAttribute("data-height")) ||
            toPositiveInt(element.getAttribute("height")) ||
            toPositiveInt(imageElement?.getAttribute("height"))
    };
}

function buildInlineImageSpanAttributes(attrs) {
    const imageId = attrs.imageId || "";
    const displayVariant = attrs.displayVariant || "original";
    const domAttrs = {
        class: "rich-text-inline-image",
        "data-rich-image-id": imageId,
        "data-display-variant": displayVariant,
        "data-alt-text": attrs.altText || "",
        contenteditable: "false"
    };

    const width = toPositiveInt(attrs.width);
    const height = toPositiveInt(attrs.height);
    if (width) {
        domAttrs["data-width"] = String(width);
    }

    if (height) {
        domAttrs["data-height"] = String(height);
    }

    return domAttrs;
}

function buildImageDomAttributes(attrs) {
    const imageId = attrs.imageId || "";
    const displayVariant = attrs.displayVariant || "original";
    const domAttrs = {
        src: attrs.src || "",
        alt: attrs.altText || "",
        "data-rich-image-id": imageId,
        "data-display-variant": displayVariant,
        loading: "lazy"
    };

    const width = toPositiveInt(attrs.width);
    const height = toPositiveInt(attrs.height);
    if (width) {
        domAttrs.width = String(width);
    }

    if (height) {
        domAttrs.height = String(height);
    }

    const styles = [];
    if (width) {
        styles.push(`width: ${width}px`);
    }

    if (height) {
        styles.push(`height: ${height}px`);
    }

    if (styles.length > 0) {
        domAttrs.style = styles.join("; ");
    }

    return domAttrs;
}

function parseRichDocumentImageUrl(src) {
    const empty = { imageId: "", variant: "original" };
    if (!src) {
        return empty;
    }

    const marker = "/rich-document-files/";
    const markerIndex = src.toLowerCase().indexOf(marker);
    if (markerIndex < 0) {
        return empty;
    }

    const tail = src.slice(markerIndex + marker.length).split(/[?#]/)[0];
    const parts = tail.split("/").filter(Boolean);
    if (parts.length < 4 || parts[1].toLowerCase() !== "images") {
        return empty;
    }

    return {
        imageId: decodeURIComponent(parts[2]),
        variant: decodeURIComponent(parts[3] || "original")
    };
}

function readVideoAttributes(element) {
    if (!element) {
        return false;
    }

    const isVideo = element.matches?.("video") === true;
    const videoElement = isVideo ? element : element.querySelector?.("video");
    const uploadToken =
        element.getAttribute("data-video-upload-token") ||
        videoElement?.getAttribute("data-video-upload-token") ||
        "";
    const uploadState =
        element.getAttribute("data-video-upload-state") ||
        videoElement?.getAttribute("data-video-upload-state") ||
        "";
    const src = videoElement?.getAttribute("src") || element.getAttribute("src") || "";
    const parsed = parseMediaServerVideoUrl(src);
    const videoId =
        element.getAttribute("data-rich-video-id") ||
        videoElement?.getAttribute("data-rich-video-id") ||
        parsed.videoId;

    if (uploadToken && uploadState) {
        return {
            src,
            videoId: videoId || "",
            title:
                element.getAttribute("data-video-title") ||
                videoElement?.getAttribute("title") ||
                element.getAttribute("title") ||
                "",
            uploadToken,
            isUploading: uploadState !== "complete",
            uploadError: element.getAttribute("data-video-upload-error") || ""
        };
    }

    if (!videoId) {
        return false;
    }

    return {
        src: src || `/media-server-files/videos/${encodeURIComponent(videoId)}/original`,
        videoId,
        title:
            element.getAttribute("data-video-title") ||
            videoElement?.getAttribute("title") ||
            element.getAttribute("title") ||
            ""
    };
}

function buildInlineVideoSpanAttributes(attrs) {
    const videoId = attrs.videoId || "";
    const result = {
        class: "rich-text-inline-video",
        "data-rich-video-id": videoId,
        "data-video-title": attrs.title || "",
        contenteditable: "false"
    };

    if (attrs.uploadToken) {
        result["data-video-upload-token"] = attrs.uploadToken;
        result["data-video-upload-state"] = attrs.isUploading ? "uploading" : "complete";
    }

    if (attrs.uploadError) {
        result["data-video-upload-error"] = attrs.uploadError;
    }

    return result;
}

function buildVideoDomAttributes(attrs) {
    const videoId = attrs.videoId || "";
    return {
        src: attrs.src || `/media-server-files/videos/${encodeURIComponent(videoId)}/original`,
        "data-rich-video-id": videoId,
        title: attrs.title || "",
        controls: "",
        preload: "metadata",
        style: "height: 300px; max-width: 100%;"
    };
}

function parseMediaServerVideoUrl(src) {
    const empty = { videoId: "" };
    if (!src) {
        return empty;
    }

    const marker = "/media-server-files/videos/";
    const markerIndex = src.toLowerCase().indexOf(marker);
    if (markerIndex < 0) {
        return empty;
    }

    const tail = src.slice(markerIndex + marker.length).split(/[?#]/)[0];
    const parts = tail.split("/").filter(Boolean);
    if (parts.length < 2 || parts[1].toLowerCase() !== "original") {
        return empty;
    }

    return {
        videoId: decodeURIComponent(parts[0])
    };
}

function getClipboardImageFile(event) {
    const items = Array.from(event?.clipboardData?.items || []);
    for (const item of items) {
        if (item.kind === "file" && item.type && item.type.startsWith("image/")) {
            const file = item.getAsFile();
            if (file) {
                return file;
            }
        }
    }

    const files = Array.from(event?.clipboardData?.files || []);
    return files.find(file => file.type && file.type.startsWith("image/")) || null;
}

async function uploadRichTextImage(documentId, file) {
    const formData = new FormData();
    formData.append("file", file, file.name || "clipboard-image.png");

    const response = await fetch(`/rich-document-files/${encodeURIComponent(documentId)}/images`, {
        method: "POST",
        body: formData
    });

    if (!response.ok) {
        const message = await response.text();
        throw new Error(message || `Image upload failed (${response.status})`);
    }

    return response.json();
}

async function insertPastedImage(editor, documentId, file) {
    const result = await uploadRichTextImage(documentId, file);
    const imageId = result.imageId ?? result.ImageId ?? "";
    const variant = result.variant ?? result.Variant ?? "original";
    const url = result.url ?? result.Url ?? `/rich-document-files/${documentId}/images/${encodeURIComponent(imageId)}/${encodeURIComponent(variant)}`;
    const fileName = result.fileName ?? result.FileName ?? file.name ?? "";

    if (!imageId) {
        throw new Error("Image upload response does not contain imageId.");
    }

    editor.chain().focus().insertContent({
        type: "richTextImage",
        attrs: {
            src: url,
            imageId,
            displayVariant: variant,
            altText: fileName,
            width: 220
        }
    }).run();
}

function handleImagePaste(viewportElementId, registry, sortOrder, event) {
    const file = getClipboardImageFile(event);
    if (!file || !registry.documentId) {
        return false;
    }

    event.preventDefault();
    const state = registry.editors.get(sortOrder);
    if (!state) {
        return true;
    }

    insertPastedImage(state.editor, registry.documentId, file).catch(error => {
        console.error("[rich-text-image-paste]", error);
    });
    return true;
}

function getClipboardPlainText(event) {
    return event?.clipboardData?.getData("text/plain") || "";
}

function getClipboardHtml(event) {
    return event?.clipboardData?.getData("text/html") || "";
}

function getClipboardTypes(event) {
    return Array.from(event?.clipboardData?.types || []);
}

function looksLikeMarkdownTable(text) {
    if (!text || !text.includes("|")) {
        return false;
    }

    const lines = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
    const separator = /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$/;
    for (let index = 1; index < lines.length; index++) {
        if (separator.test(lines[index]) && countUnescapedPipes(lines[index - 1]) >= 1) {
            return true;
        }
    }

    return false;
}

function looksLikeMarkdown(text) {
    if (!text) {
        return false;
    }

    const normalized = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    if (looksLikeMarkdownTable(normalized)) {
        return true;
    }

    const lines = normalized.split("\n");
    if (lines.some(line => /^\s{0,3}#{1,6}\s+\S/.test(line) || /^\s{0,3}(```|~~~)/.test(line))) {
        return true;
    }

    if (lines.filter(line => /^\s{0,3}([-*+]|\d+[.)])\s+\S/.test(line)).length >= 2) {
        return true;
    }

    return /\[[^\]]+\]\([^)]+\)/.test(normalized) || /`[^`\r\n]+`/.test(normalized);
}

function looksLikeSupportedClipboardHtml(html) {
    return /<\s*(table|thead|tbody|tfoot|tr|td|th|h[1-6]|p|div|ul|ol|li|blockquote|pre|code|img|span|strong|b|em|i|u|br)\b/i
        .test(html || "");
}

function countUnescapedPipes(value) {
    let count = 0;
    let escaped = false;
    for (const char of String(value || "")) {
        if (escaped) {
            escaped = false;
            continue;
        }

        if (char === "\\") {
            escaped = true;
            continue;
        }

        if (char === "|") {
            count++;
        }
    }

    return count;
}

function escapeHtml(value) {
    return String(value || "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function shouldHandleClipboardImportPaste(event) {
    const plainText = getClipboardPlainText(event);
    if (looksLikeMarkdown(plainText)) {
        return true;
    }

    return looksLikeSupportedClipboardHtml(getClipboardHtml(event));
}

function buildClipboardPastePayload(event) {
    return {
        PlainText: getClipboardPlainText(event),
        Html: getClipboardHtml(event),
        Types: getClipboardTypes(event)
    };
}

function insertClipboardFallback(editor, payload) {
    const text = payload?.PlainText || "";
    if (!text) {
        return;
    }

    const html = text
        .replace(/\r\n/g, "\n")
        .replace(/\r/g, "\n")
        .split(/\n{2,}/)
        .map(paragraph => `<p>${escapeHtml(paragraph).replace(/\n/g, "<br />")}</p>`)
        .join("");

    if (html) {
        editor.chain().focus().insertContent(html).run();
    }
}

function handleClipboardImportPaste(viewportElementId, registry, sortOrder, event) {
    if (!registry.dotNetReference || !shouldHandleClipboardImportPaste(event)) {
        return false;
    }

    const state = registry.editors.get(sortOrder);
    if (!state?.editor) {
        return false;
    }

    const payload = buildClipboardPastePayload(event);
    event.preventDefault();
    registry.dotNetReference
        .invokeMethodAsync("OnClipboardPasteAsync", payload)
        .then(result => {
            const handled = result?.handled ?? result?.Handled ?? false;
            const html = result?.html ?? result?.Html ?? "";
            const currentState = registry.editors.get(sortOrder);
            const editor = currentState?.editor;
            if (!editor) {
                return;
            }

            if (handled && html) {
                editor.chain().focus().insertContent(html).run();
                return;
            }

            insertClipboardFallback(editor, payload);
        })
        .catch(error => {
            console.error("[rich-text-clipboard-paste]", error);
            const currentState = registry.editors.get(sortOrder);
            if (currentState?.editor) {
                insertClipboardFallback(currentState.editor, payload);
            }
        });

    return true;
}

function getDataTransferVideoFile(event) {
    const files = Array.from(event?.dataTransfer?.files || []);
    return files.find(file => file.type && file.type.startsWith("video/")) || null;
}

async function uploadMediaVideo(file) {
    const formData = new FormData();
    formData.append("file", file, file.name || "video");

    const response = await fetch("/media-server-files/videos", {
        method: "POST",
        body: formData
    });

    if (!response.ok) {
        const message = await response.text();
        throw new Error(message || `Video upload failed (${response.status})`);
    }

    return response.json();
}

function insertVideoNode(editor, videoId, title, url) {
    if (!editor || !videoId) {
        return;
    }

    editor.chain().focus().insertContent({
        type: "richTextVideo",
        attrs: {
            src: url || `/media-server-files/videos/${encodeURIComponent(videoId)}/original`,
            videoId,
            title: title || ""
        }
    }).run();
}

function insertVideoUploadPlaceholderNode(editor, uploadToken, title) {
    if (!editor || !uploadToken) {
        return;
    }

    editor.chain().focus().insertContent({
        type: "richTextVideo",
        attrs: {
            src: "",
            videoId: "",
            title: title || "Видео загружается",
            uploadToken,
            isUploading: true,
            uploadError: ""
        }
    }).run();
}

function updateVideoUploadPlaceholderNode(editor, uploadToken, attrs) {
    if (!editor || !uploadToken) {
        return false;
    }

    let targetPosition = null;
    editor.state.doc.descendants((node, position) => {
        if (node.type.name === "richTextVideo" && node.attrs.uploadToken === uploadToken) {
            targetPosition = position;
            return false;
        }

        return true;
    });

    if (targetPosition == null) {
        return false;
    }

    const currentNode = editor.state.doc.nodeAt(targetPosition);
    if (!currentNode) {
        return false;
    }

    const transaction = editor.state.tr.setNodeMarkup(targetPosition, undefined, {
        ...currentNode.attrs,
        ...attrs
    });
    editor.view.dispatch(transaction);
    return true;
}

async function insertDroppedVideo(editor, file) {
    const result = await uploadMediaVideo(file);
    const videoId = result.id ?? result.Id ?? "";
    const title = result.displayName ?? result.DisplayName ?? file.name ?? "";
    const url = result.embedUrl ?? result.EmbedUrl ?? `/media-server-files/videos/${encodeURIComponent(videoId)}/original`;

    if (!videoId) {
        throw new Error("Video upload response does not contain id.");
    }

    insertVideoNode(editor, videoId, title, url);
}

function handleVideoDrop(registry, sortOrder, event) {
    const file = getDataTransferVideoFile(event);
    if (!file) {
        return false;
    }

    event.preventDefault();
    const state = registry.editors.get(sortOrder);
    if (!state) {
        return true;
    }

    insertDroppedVideo(state.editor, file).catch(error => {
        console.error("[rich-text-video-drop]", error);
    });
    return true;
}

function hideImageSizeMenu() {
    if (!activeImageMenu) {
        return;
    }

    const menu = activeImageMenu;
    activeImageMenu = null;
    document.removeEventListener("mousedown", menu.onDocumentMouseDown, true);
    document.removeEventListener("keydown", menu.onDocumentKeyDown, true);
    window.removeEventListener("resize", menu.onWindowChange, true);
    window.removeEventListener("scroll", menu.onWindowChange, true);
    menu.element.remove();
}

function hideCellBackgroundPalette() {
    if (!activeCellBackgroundPalette) {
        return;
    }

    const palette = activeCellBackgroundPalette;
    activeCellBackgroundPalette = null;
    document.removeEventListener("mousedown", palette.onDocumentMouseDown, true);
    document.removeEventListener("keydown", palette.onDocumentKeyDown, true);
    window.removeEventListener("resize", palette.onWindowChange, true);
    window.removeEventListener("scroll", palette.onWindowChange, true);
    palette.element.remove();
}

function handleImageContextMenu(registry, sortOrder, event) {
    const target = event.target instanceof Element ? event.target : null;
    const image = target?.closest("span.rich-text-inline-image, span.rich-text-inline-image img, img[data-rich-image-id], p.rich-text-image img");
    const state = registry.editors.get(sortOrder);
    if (!image || !state || !state.editor?.view?.dom?.contains(image)) {
        return false;
    }

    const position = findImageNodePosition(state.editor, image);
    if (position == null) {
        return false;
    }

    event.preventDefault();
    event.stopPropagation();
    showImageSizeMenu(state.editor, position, event);
    return true;
}

function findImageNodePosition(editor, imageElement) {
    const imageAttrs = readImageAttributes(imageElement);
    if (!imageAttrs || !imageAttrs.imageId) {
        return null;
    }

    const markerElement = imageElement.closest?.("span.rich-text-inline-image") || imageElement;
    try {
        const domPosition = editor.view.posAtDOM(markerElement, 0);
        for (const offset of [0, -1, 1]) {
            const position = domPosition + offset;
            const node = position >= 0 ? editor.state.doc.nodeAt(position) : null;
            if (node?.type?.name === "richTextImage") {
                return position;
            }
        }
    } catch {
        // Fallback below handles legacy DOM or browser-specific posAtDOM quirks.
    }

    let match = null;
    editor.state.doc.descendants((node, position) => {
        if (node.type.name !== "richTextImage") {
            return true;
        }

        if (node.attrs.imageId === imageAttrs.imageId) {
            match = position;
            return false;
        }

        return true;
    });

    return match;
}

function showImageSizeMenu(editor, position, event) {
    hideImageSizeMenu();
    hideCellBackgroundPalette();

    const menu = document.createElement("div");
    menu.style.position = "fixed";
    menu.style.zIndex = "10000";
    menu.style.minWidth = "0";
    menu.style.padding = "6px";
    menu.style.border = "1px solid #b8c4d4";
    menu.style.borderRadius = "4px";
    menu.style.background = "#ffffff";
    menu.style.boxShadow = "0 8px 24px rgba(15, 23, 42, 0.18)";
    menu.style.font = "13px/1.3 Arial, sans-serif";
    menu.style.color = "#102033";
    menu.style.userSelect = "none";

    const optionRow = document.createElement("div");
    optionRow.style.display = "flex";
    optionRow.style.alignItems = "center";
    optionRow.style.gap = "4px";

    for (const option of [
        { label: "100px", width: 100 },
        { label: "200px", width: 200 },
        { label: "300px", width: 300 },
        { label: "500px", width: 500 },
        { label: "[orig]", width: null }
    ]) {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = option.label;
        button.style.border = "1px solid #c9d4e2";
        button.style.borderRadius = "3px";
        button.style.background = "#f8fafc";
        button.style.padding = "4px 6px";
        button.style.cursor = "pointer";
        button.style.color = "#102033";
        button.addEventListener("mousedown", e => e.preventDefault());
        button.addEventListener("click", () => setImageWidth(editor, position, option.width));
        optionRow.appendChild(button);
    }

    const input = document.createElement("input");
    input.type = "number";
    input.min = "1";
    input.step = "1";
    input.placeholder = "custom px";
    input.style.boxSizing = "border-box";
    input.style.width = "92px";
    input.style.marginLeft = "4px";
    input.style.border = "1px solid #c9d4e2";
    input.style.borderRadius = "3px";
    input.style.padding = "5px 6px";
    input.addEventListener("mousedown", e => e.stopPropagation());
    input.addEventListener("keydown", e => {
        if (e.key === "Enter") {
            e.preventDefault();
            const width = toPositiveInt(input.value);
            if (width) {
                setImageWidth(editor, position, width);
            }
        }
    });

    optionRow.appendChild(input);
    menu.appendChild(optionRow);
    document.body.appendChild(menu);

    positionImageMenu(menu, event.clientX, event.clientY);

    const menuState = {
        element: menu,
        onDocumentMouseDown: e => {
            if (!menu.contains(e.target)) {
                hideImageSizeMenu();
            }
        },
        onDocumentKeyDown: e => {
            if (e.key === "Escape") {
                hideImageSizeMenu();
            }
        },
        onWindowChange: () => hideImageSizeMenu()
    };

    activeImageMenu = menuState;
    document.addEventListener("mousedown", menuState.onDocumentMouseDown, true);
    document.addEventListener("keydown", menuState.onDocumentKeyDown, true);
    window.addEventListener("resize", menuState.onWindowChange, true);
    window.addEventListener("scroll", menuState.onWindowChange, true);
    input.focus();
}

function positionImageMenu(menu, clientX, clientY) {
    const margin = 8;
    const rect = menu.getBoundingClientRect();
    const left = Math.min(clientX, window.innerWidth - rect.width - margin);
    const top = Math.min(clientY, window.innerHeight - rect.height - margin);
    menu.style.left = `${Math.max(margin, left)}px`;
    menu.style.top = `${Math.max(margin, top)}px`;
}

function setImageWidth(editor, position, width) {
    const node = editor.state.doc.nodeAt(position);
    if (!node || node.type.name !== "richTextImage") {
        hideImageSizeMenu();
        return;
    }

    const nextAttrs = {
        ...node.attrs,
        width: width ?? null,
        height: null
    };

    const transaction = editor.state.tr.setNodeMarkup(position, undefined, nextAttrs);
    editor.view.dispatch(transaction);
    editor.view.focus();
    hideImageSizeMenu();
}

function showCellBackgroundPalette(viewportElementId, registry) {
    hideCellBackgroundPalette();
    hideImageSizeMenu();

    const editor = getActiveEditor(registry);
    if (!editor || !findSelectedTable(editor)) {
        return;
    }

    const toolbar = getEditorToolbar(viewportElementId);
    const trigger = toolbar?.querySelector("[data-rich-text-editor-command=\"openCellBackgroundPalette\"]");
    if (!trigger) {
        return;
    }

    const menu = document.createElement("div");
    menu.className = "rich-text-cell-background-palette";
    menu.setAttribute("role", "menu");
    menu.setAttribute("aria-label", "Заливка ячеек");

    const currentColor = getCurrentCellBackground(editor);
    for (const item of cellBackgroundPalette) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = item.color
            ? "rich-text-cell-background-palette__item"
            : "rich-text-cell-background-palette__item rich-text-cell-background-palette__item--clear";
        button.title = item.label;
        button.setAttribute("aria-label", item.label);
        button.setAttribute("role", "menuitem");
        button.setAttribute("data-rich-cell-background-color", item.color ?? "");
        button.classList.toggle("rich-text-cell-background-palette__item--selected", currentColor === item.color);
        button.addEventListener("mousedown", event => event.preventDefault());
        button.addEventListener("click", () => setSelectedCellBackground(viewportElementId, registry, item.color));

        const swatch = document.createElement("span");
        swatch.className = item.color
            ? "rich-text-cell-background-palette__swatch"
            : "rich-text-cell-background-palette__swatch rich-text-cell-background-palette__swatch--clear";
        if (item.color) {
            swatch.style.backgroundColor = item.color;
        }

        button.appendChild(swatch);
        if (!item.color) {
            const label = document.createElement("span");
            label.className = "rich-text-cell-background-palette__clear-label";
            label.textContent = "Сброс";
            button.appendChild(label);
        }

        menu.appendChild(button);
    }

    document.body.appendChild(menu);
    positionPaletteNearElement(menu, trigger);

    const paletteState = {
        element: menu,
        onDocumentMouseDown: event => {
            if (!menu.contains(event.target) && !trigger.contains(event.target)) {
                hideCellBackgroundPalette();
            }
        },
        onDocumentKeyDown: event => {
            if (event.key === "Escape") {
                hideCellBackgroundPalette();
            }
        },
        onWindowChange: () => hideCellBackgroundPalette()
    };

    activeCellBackgroundPalette = paletteState;
    document.addEventListener("mousedown", paletteState.onDocumentMouseDown, true);
    document.addEventListener("keydown", paletteState.onDocumentKeyDown, true);
    window.addEventListener("resize", paletteState.onWindowChange, true);
    window.addEventListener("scroll", paletteState.onWindowChange, true);
}

function positionPaletteNearElement(menu, trigger) {
    const margin = 8;
    const triggerRect = trigger.getBoundingClientRect();
    const menuRect = menu.getBoundingClientRect();
    const left = Math.min(triggerRect.left, window.innerWidth - menuRect.width - margin);
    const top = Math.min(triggerRect.bottom + 4, window.innerHeight - menuRect.height - margin);
    menu.style.left = `${Math.max(margin, left)}px`;
    menu.style.top = `${Math.max(margin, top)}px`;
}

function setSelectedCellBackground(viewportElementId, registry, color) {
    const editor = getActiveEditor(registry);
    if (!editor) {
        hideCellBackgroundPalette();
        return;
    }

    const normalizedColor = normalizeCellBackground(color);
    if (!findSelectedTable(editor)) {
        hideCellBackgroundPalette();
        return;
    }

    editor.chain().focus().setCellAttribute("cellBackground", normalizedColor).run();
    syncToolbarState(viewportElementId, registry, editor);
    hideCellBackgroundPalette();
}

function getCurrentCellBackground(editor) {
    if (!editor) {
        return null;
    }

    const selection = editor.state.selection;
    const selectedCellNode = selection?.$anchorCell?.nodeAfter ?? null;
    if (selectedCellNode?.type?.name === "tableCell" || selectedCellNode?.type?.name === "tableHeader") {
        return normalizeCellBackground(selectedCellNode.attrs?.cellBackground);
    }

    const resolved = selection?.$from;
    if (!resolved) {
        return null;
    }

    for (let depth = resolved.depth; depth > 0; depth--) {
        const node = resolved.node(depth);
        if (node?.type?.name === "tableCell" || node?.type?.name === "tableHeader") {
            return normalizeCellBackground(node.attrs?.cellBackground);
        }
    }

    return null;
}

function createEditor(viewportElementId, registry, host, item) {
    const sortOrder = toSortOrder(item.sortOrder ?? item.SortOrder);
    const chunkId = item.chunkId ?? item.ChunkId ?? "";
    const html = item.html ?? item.Html ?? "";
    const providedOriginalHtml = item.originalHtml ?? item.OriginalHtml ?? null;
    const isDraft = Boolean(item.isDraft ?? item.IsDraft ?? false);

    let editor = null;
    editor = new Editor({
        element: host,
        extensions,
        content: html || "<p></p>",
        editorProps: {
            attributes: {
                class: "rich-text-tiptap-content"
            },
            handlePaste: (view, event) => {
                if (handleImagePaste(viewportElementId, registry, sortOrder, event)) {
                    return true;
                }

                return handleClipboardImportPaste(viewportElementId, registry, sortOrder, event);
            },
            handleDrop: (view, event) => {
                return handleVideoDrop(registry, sortOrder, event);
            },
            handleDOMEvents: {
                contextmenu: (view, event) => {
                    return handleImageContextMenu(registry, sortOrder, event);
                }
            }
        },
        onFocus: () => {
            registry.activeSortOrder = sortOrder;
            syncToolbarState(viewportElementId, registry, editor);
        },
        onSelectionUpdate: () => {
            registry.activeSortOrder = sortOrder;
            syncToolbarState(viewportElementId, registry, editor);
        },
        onUpdate: () => {
            const state = registry.editors.get(sortOrder);
            if (state) {
                const wasDirty = state.isDirty === true;
                state.isDirty = true;
                if (registry.dotNetReference) {
                    registry.dotNetReference.invokeMethodAsync(
                        "OnEditorChunkEdited",
                        {
                            chunkId: state.chunkId,
                            sortOrder: state.sortOrder,
                            originalHtml: state.originalHtml,
                            html: serializeEditorHtml(state.editor),
                            isDirty: true
                        },
                        !wasDirty).catch(() => {});
                }
            }

            syncToolbarState(viewportElementId, registry, editor);
        }
    });

    const state = {
        editor,
        chunkId,
        sortOrder,
        originalHtml: providedOriginalHtml == null
            ? serializeEditorHtml(editor)
            : providedOriginalHtml,
        isDirty: isDraft,
        disposeTableDebugging: attachTableDebugging(editor, host, registry, sortOrder),
        disposeTableResizeHandleGuard: attachTableResizeHandleGuard(host)
    };

    registry.editors.set(sortOrder, state);
    host.dataset.richTextEditorInitialized = "true";
    ensureToolbarGuard(viewportElementId, registry);
    if (registry.activeSortOrder === sortOrder) {
        syncToolbarState(viewportElementId, registry, editor);
    }
}

function notifyEditorDisposed(registry, state, reason) {
    if (!registry.dotNetReference || !state) {
        return;
    }

    registry.dotNetReference
        .invokeMethodAsync(
            "OnEditorChunkDisposed",
            String(state.chunkId ?? ""),
            state.sortOrder,
            state.isDirty === true,
            reason)
        .catch(() => {});
}

function syncEditors(viewportElementId, chunks, dotNetReference, documentId) {
    const viewport = document.getElementById(viewportElementId);
    if (!viewport) {
        return;
    }

    const registry = getRegistry(viewportElementId);
    registry.dotNetReference = dotNetReference ?? registry.dotNetReference;
    registry.documentId = documentId ?? registry.documentId;
    ensureToolbarGuard(viewportElementId, registry);
    const visibleSortOrders = new Set();
    const items = Array.isArray(chunks) ? chunks : [];
    const itemsBySortOrder = new Map();

    for (const item of items) {
        const sortOrder = toSortOrder(item.sortOrder ?? item.SortOrder);
        if (sortOrder < 0) {
            continue;
        }

        visibleSortOrders.add(sortOrder);
        itemsBySortOrder.set(sortOrder, item);
    }

    for (const [sortOrder, state] of Array.from(registry.editors.entries())) {
        const hostStillExists = viewport.querySelector(`[data-rich-text-editor-host][data-chunk-sort-order="${sortOrder}"]`);
        const editorDomStillAttached = hostStillExists && state.editor?.view?.dom && hostStillExists.contains(state.editor.view.dom);
        if (visibleSortOrders.has(sortOrder) && hostStillExists && editorDomStillAttached) {
            continue;
        }

        if (visibleSortOrders.has(sortOrder) && hostStillExists && state.isDirty === true) {
            itemsBySortOrder.set(sortOrder, {
                chunkId: state.chunkId,
                sortOrder: state.sortOrder,
                html: serializeEditorHtml(state.editor),
                originalHtml: state.originalHtml,
                isDraft: true
            });
        }

        notifyEditorDisposed(registry, state, "sync-window");
        state.disposeTableDebugging?.();
        state.disposeTableResizeHandleGuard?.();
        state.editor.destroy();
        registry.editors.delete(sortOrder);
    }

    const hosts = viewport.querySelectorAll("[data-rich-text-editor-host]");
    hosts.forEach(host => {
        const sortOrder = toSortOrder(host.getAttribute("data-chunk-sort-order"));
        if (sortOrder < 0 || registry.editors.has(sortOrder)) {
            return;
        }

        const item = itemsBySortOrder.get(sortOrder);
        if (!item) {
            return;
        }

        createEditor(viewportElementId, registry, host, item);
    });
}

function collectEditors(viewportElementId) {
    const registry = registries.get(viewportElementId);
    if (!registry) {
        return [];
    }

    return Array.from(registry.editors.values()).map(state => {
        const currentHtml = serializeEditorHtml(state.editor);
        const isDirty = state.isDirty === true || currentHtml !== state.originalHtml;
        const html = isDirty ? currentHtml : "";
        return {
            chunkId: state.chunkId,
            sortOrder: state.sortOrder,
            originalHtml: isDirty ? state.originalHtml : "",
            html: isDirty ? html : "",
            isDirty
        };
    });
}

function destroyEditors(viewportElementId) {
    hideImageSizeMenu();
    hideCellBackgroundPalette();

    const registry = registries.get(viewportElementId);
    if (!registry) {
        return;
    }

    for (const state of registry.editors.values()) {
        notifyEditorDisposed(registry, state, "destroy-viewport");
        state.disposeTableDebugging?.();
        state.disposeTableResizeHandleGuard?.();
        state.editor.destroy();
    }

    registries.delete(viewportElementId);
}

function markClean(viewportElementId, sortOrders) {
    const registry = registries.get(viewportElementId);
    if (!registry) {
        return;
    }

    const normalizedSortOrders = new Set((Array.isArray(sortOrders) ? sortOrders : [])
        .map(toSortOrder)
        .filter(sortOrder => sortOrder >= 0));

    for (const [sortOrder, state] of registry.editors.entries()) {
        if (normalizedSortOrders.size > 0 && !normalizedSortOrders.has(sortOrder)) {
            continue;
        }

        state.originalHtml = serializeEditorHtml(state.editor);
        state.isDirty = false;
    }
}

function getActiveEditor(registry) {
    if (registry.activeSortOrder != null && registry.editors.has(registry.activeSortOrder)) {
        return registry.editors.get(registry.activeSortOrder).editor;
    }

    const first = registry.editors.values().next();
    return first.done ? null : first.value.editor;
}

function runCommand(viewportElementId, command) {
    const registry = registries.get(viewportElementId);
    if (!registry) {
        return;
    }

    if (command === "openCellBackgroundPalette") {
        showCellBackgroundPalette(viewportElementId, registry);
        return;
    }

    const editor = getActiveEditor(registry);
    if (!editor) {
        return;
    }

    switch (command) {
        case "toggleBold":
            editor.chain().focus().toggleBold().run();
            break;
        case "toggleItalic":
            editor.chain().focus().toggleItalic().run();
            break;
        case "toggleUnderline":
            editor.chain().focus().toggleUnderline().run();
            break;
        case "setParagraph":
            editor.chain().focus().setParagraph().run();
            break;
        case "toggleHeading1":
            editor.chain().focus().toggleHeading({ level: 1 }).run();
            break;
        case "toggleHeading2":
            editor.chain().focus().toggleHeading({ level: 2 }).run();
            break;
        case "toggleHeading3":
            editor.chain().focus().toggleHeading({ level: 3 }).run();
            break;
        case "insertTable":
            insertTableWithPrompt(editor);
            break;
        case "toggleTableHeaderRow":
            toggleFirstTableHeaderRow(editor);
            break;
        case "toggleTableRowNumbers":
            toggleTableRowNumbers(editor);
            break;
        case "addTableRow":
            editor.chain().focus().addRowAfter().run();
            renumberSelectedTableIfEnabled(editor);
            break;
        case "deleteTableRow":
            editor.chain().focus().deleteRow().run();
            renumberSelectedTableIfEnabled(editor);
            break;
        case "addTableColumn":
            editor.chain().focus().addColumnAfter().run();
            renumberSelectedTableIfEnabled(editor);
            break;
        case "deleteTableColumn":
            editor.chain().focus().deleteColumn().run();
            renumberSelectedTableIfEnabled(editor);
            break;
        case "deleteTable":
            editor.chain().focus().deleteTable().run();
            break;
    }

    syncToolbarState(viewportElementId, registry, editor);
}

function insertTableWithPrompt(editor) {
    if (!editor) {
        return;
    }

    const sizeText = window.prompt("Размер таблицы: строки x столбцы", "3x3");
    if (!sizeText) {
        return;
    }

    const match = String(sizeText).trim().match(/^(\d+)\s*[xх*,;:]\s*(\d+)$/i);
    if (!match) {
        return;
    }

    const rows = Math.max(1, Math.min(50, Number(match[1])));
    const cols = Math.max(1, Math.min(20, Number(match[2])));
    if (!Number.isFinite(rows) || !Number.isFinite(cols)) {
        return;
    }

    editor.chain().focus().insertTable({
        rows,
        cols,
        withHeaderRow: false
    }).run();
}

function toggleFirstTableHeaderRow(editor) {
    if (!editor) {
        return;
    }

    const tableInfo = findSelectedTable(editor);
    if (!tableInfo || tableInfo.node.childCount === 0) {
        return;
    }

    const tableCellType = editor.schema.nodes.tableCell;
    const tableHeaderType = editor.schema.nodes.tableHeader;
    if (!tableCellType || !tableHeaderType) {
        return;
    }

    const firstRow = tableInfo.node.firstChild;
    if (!firstRow || firstRow.childCount === 0) {
        return;
    }

    let hasOnlyHeaderCells = true;
    firstRow.forEach(cell => {
        if (cell.type !== tableHeaderType) {
            hasOnlyHeaderCells = false;
        }
    });

    const targetType = hasOnlyHeaderCells ? tableCellType : tableHeaderType;
    let transaction = editor.state.tr;
    let cellPosition = tableInfo.position + 2;

    firstRow.forEach(cell => {
        transaction = transaction.setNodeMarkup(
            cellPosition,
            targetType,
            cell.attrs,
            cell.marks);
        cellPosition += cell.nodeSize;
    });

    if (transaction.docChanged) {
        dispatchTransactionKeepingSelection(editor, transaction);
        renumberSelectedTableIfEnabled(editor);
    }
}

function toggleTableRowNumbers(editor) {
    if (!editor) {
        return;
    }

    const tableInfo = findSelectedTable(editor);
    if (!tableInfo) {
        return;
    }

    setTableRowNumbers(editor, !isTableRowNumberingEnabled(tableInfo.node));
}

function renumberSelectedTableIfEnabled(editor) {
    const tableInfo = findSelectedTable(editor);
    if (!tableInfo || !isTableRowNumberingEnabled(tableInfo.node)) {
        return;
    }

    setTableRowNumbers(editor, true);
}

function setTableRowNumbers(editor, enabled) {
    const tableInfo = findSelectedTable(editor);
    if (!tableInfo) {
        return;
    }

    const tableNode = tableInfo.node;
    const tableCellType = editor.schema.nodes.tableCell;
    const tableHeaderType = editor.schema.nodes.tableHeader;
    const paragraphType = editor.schema.nodes.paragraph;
    if (!tableCellType || !tableHeaderType || !paragraphType) {
        return;
    }

    const wasEnabled = isTableRowNumberingEnabled(tableNode);
    const cursorContext = captureTableCursorContext(editor, tableInfo, wasEnabled);
    const headerEnabled = isFirstTableRowHeader(tableNode, tableHeaderType);
    const rowsWithoutNumbers = getRowsWithoutNumberColumn(tableNode, tableCellType, tableHeaderType, paragraphType);
    const nextRows = enabled
        ? rowsWithoutNumbers.map((row, rowIndex) => {
            const numberCellType = headerEnabled && rowIndex === 0 ? tableHeaderType : tableCellType;
            const label = headerEnabled && rowIndex === 0 ? "№" : String(headerEnabled ? rowIndex : rowIndex + 1);
            const numberCell = createTextTableCell(numberCellType, paragraphType, label, true);
            return row.type.create(row.attrs, [numberCell, ...Array.from(row.content.content)], row.marks);
        })
        : rowsWithoutNumbers;

    const nextTable = tableNode.type.create(
        {
            ...tableNode.attrs,
            rowNumbers: enabled
        },
        nextRows,
        tableNode.marks);

    const transaction = editor.state.tr.replaceWith(
        tableInfo.position,
        tableInfo.position + tableNode.nodeSize,
        nextTable);

    dispatchTransactionKeepingSelection(
        editor,
        transaction,
        getTableCursorRestorePosition(nextTable, tableInfo.position, cursorContext, enabled));
}

function captureTableCursorContext(editor, tableInfo, rowNumbersEnabled) {
    const selection = editor?.state?.selection;
    const resolved = selection?.$from;
    if (!resolved || !tableInfo) {
        return null;
    }

    let tableDepth = -1;
    let rowDepth = -1;
    let cellDepth = -1;
    for (let depth = resolved.depth; depth > 0; depth--) {
        const nodeName = resolved.node(depth)?.type?.name;
        if (nodeName === "table" && tableDepth < 0) {
            tableDepth = depth;
        } else if (nodeName === "tableRow" && rowDepth < 0) {
            rowDepth = depth;
        } else if ((nodeName === "tableCell" || nodeName === "tableHeader") && cellDepth < 0) {
            cellDepth = depth;
        }
    }

    if (tableDepth < 0 || rowDepth < 0 || cellDepth < 0) {
        return null;
    }

    const rowIndex = resolved.index(tableDepth);
    const cellIndex = resolved.index(rowDepth);
    const cellStart = resolved.before(cellDepth);
    const offsetInsideCell = Math.max(0, resolved.pos - (cellStart + 1));

    return {
        rowIndex,
        dataCellIndex: rowNumbersEnabled && cellIndex > 0 ? cellIndex - 1 : cellIndex,
        offsetInsideCell
    };
}

function getTableCursorRestorePosition(tableNode, tablePosition, cursorContext, rowNumbersEnabled) {
    if (!tableNode || !cursorContext || tableNode.childCount === 0) {
        return null;
    }

    const rowIndex = Math.min(Math.max(cursorContext.rowIndex, 0), tableNode.childCount - 1);
    const rowInfo = getChildNodeAndOffset(tableNode, rowIndex);
    const row = rowInfo?.node;
    if (!row || row.childCount === 0) {
        return null;
    }

    const desiredCellIndex = rowNumbersEnabled
        ? cursorContext.dataCellIndex + 1
        : cursorContext.dataCellIndex;
    const cellIndex = Math.min(Math.max(desiredCellIndex, 0), row.childCount - 1);
    const cellInfo = getChildNodeAndOffset(row, cellIndex);
    const cell = cellInfo?.node;
    if (!cell) {
        return null;
    }

    const cellPosition = tablePosition + 1 + rowInfo.offset + 1 + cellInfo.offset;
    const cellContentStart = cellPosition + 1;
    const cellContentEnd = Math.max(cellContentStart, cellPosition + cell.nodeSize - 1);
    return Math.min(cellContentStart + Math.max(cursorContext.offsetInsideCell, 0), cellContentEnd);
}

function getChildNodeAndOffset(parentNode, childIndex) {
    if (!parentNode || childIndex < 0 || childIndex >= parentNode.childCount) {
        return null;
    }

    let result = null;
    parentNode.forEach((node, offset, index) => {
        if (index === childIndex) {
            result = { node, offset };
        }
    });

    return result;
}

function dispatchTransactionKeepingSelection(editor, transaction, targetPosition = null) {
    if (!editor || !transaction) {
        return;
    }

    const previousSelection = editor.state.selection;
    if (transaction.docChanged && previousSelection) {
        try {
            transaction = transaction.setSelection(
                targetPosition == null
                    ? previousSelection.map(transaction.doc, transaction.mapping)
                    : TextSelection.near(transaction.doc.resolve(targetPosition), 1));
        } catch {
            // If ProseMirror cannot map the exact cursor position, dispatch the document change anyway.
        }
    }

    editor.view.dispatch(transaction);
    editor.view.focus();
}

function getRowsWithoutNumberColumn(tableNode, tableCellType, tableHeaderType, paragraphType) {
    const headerEnabled = isFirstTableRowHeader(tableNode, tableHeaderType);
    const result = [];

    tableNode.forEach((row, _offset, rowIndex) => {
        const cells = Array.from(row.content.content);
        const firstCell = cells[0];
        const hasGeneratedNumberCell = firstCell?.attrs?.rowNumber === true || tableNode.attrs?.rowNumbers === true;
        const remainingCells = hasGeneratedNumberCell ? cells.slice(1) : cells;

        if (remainingCells.length === 0) {
            const fallbackType = headerEnabled && rowIndex === 0 ? tableHeaderType : tableCellType;
            remainingCells.push(createTextTableCell(fallbackType, paragraphType, "", false));
        }

        result.push(row.type.create(row.attrs, remainingCells, row.marks));
    });

    return result;
}

function createTextTableCell(cellType, paragraphType, text, rowNumber) {
    const paragraph = text
        ? paragraphType.create(null, cellType.schema.text(text))
        : paragraphType.create();

    return cellType.create(
        {
            rowNumber
        },
        paragraph);
}

function isTableRowNumberingEnabled(tableNode) {
    if (tableNode?.attrs?.rowNumbers === true) {
        return true;
    }

    let enabled = false;
    tableNode?.forEach(row => {
        if (row.firstChild?.attrs?.rowNumber === true) {
            enabled = true;
        }
    });

    return enabled;
}

function isFirstTableRowHeader(tableNode, tableHeaderType) {
    const firstRow = tableNode?.firstChild;
    if (!firstRow || firstRow.childCount === 0) {
        return false;
    }

    let result = true;
    firstRow.forEach(cell => {
        if (cell.type !== tableHeaderType || cell.attrs?.rowNumber === true) {
            result = false;
        }
    });

    if (!result && firstRow.childCount > 1 && firstRow.firstChild?.attrs?.rowNumber === true) {
        result = true;
        firstRow.forEach((cell, _offset, index) => {
            if (index > 0 && cell.type !== tableHeaderType) {
                result = false;
            }
        });
    }

    return result;
}

function findSelectedTable(editor) {
    const selection = editor?.state?.selection;
    if (!selection) {
        return null;
    }

    const resolved = selection.$from;
    for (let depth = resolved.depth; depth > 0; depth--) {
        const node = resolved.node(depth);
        if (node?.type?.name === "table") {
            return {
                node,
                position: resolved.before(depth)
            };
        }
    }

    return null;
}

function insertVideo(viewportElementId, videoId, title, url) {
    const registry = registries.get(viewportElementId);
    if (!registry) {
        return;
    }

    const editor = getActiveEditor(registry);
    insertVideoNode(editor, videoId, title, url);
}

function insertVideoUploadPlaceholder(viewportElementId, uploadToken, title) {
    const registry = registries.get(viewportElementId);
    if (!registry) {
        return;
    }

    const editor = getActiveEditor(registry);
    insertVideoUploadPlaceholderNode(editor, uploadToken, title);
}

function completeVideoUploadPlaceholder(viewportElementId, uploadToken, videoId, title, url) {
    const registry = registries.get(viewportElementId);
    if (!registry || !videoId) {
        return;
    }

    for (const state of registry.editors.values()) {
        if (updateVideoUploadPlaceholderNode(state.editor, uploadToken, {
            src: url || `/media-server-files/videos/${encodeURIComponent(videoId)}/original`,
            videoId,
            title: title || "",
            uploadToken: null,
            isUploading: false,
            uploadError: ""
        })) {
            return;
        }
    }
}

function failVideoUploadPlaceholder(viewportElementId, uploadToken, errorMessage) {
    const registry = registries.get(viewportElementId);
    if (!registry) {
        return;
    }

    for (const state of registry.editors.values()) {
        if (updateVideoUploadPlaceholderNode(state.editor, uploadToken, {
            isUploading: true,
            uploadError: errorMessage || "Не удалось загрузить видео"
        })) {
            return;
        }
    }
}

function enableTableDebug(enabled = true) {
    window.richDocTableDebug = enabled === true;
    try {
        if (enabled === true) {
            window.localStorage?.setItem("richDocTableDebug", "1");
        } else {
            window.localStorage?.removeItem("richDocTableDebug");
        }
    } catch {
        // The in-memory flag above still works if localStorage is unavailable.
    }
}

window.richTextEditor = {
    syncEditors,
    collectEditors,
    destroyEditors,
    markClean,
    runCommand,
    insertVideo,
    insertVideoUploadPlaceholder,
    completeVideoUploadPlaceholder,
    failVideoUploadPlaceholder,
    enableTableDebug
};

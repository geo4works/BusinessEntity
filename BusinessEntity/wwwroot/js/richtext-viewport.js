function createRichTextViewportRuntime() {
    const registry = new Map();
    const blockSelector = "h1,h2,h3,h4,h5,h6,p,blockquote,pre,ul,ol,table,hr";
    let activeImageViewer = null;

    function getViewportBottomReservePx(viewport) {
        const reserve = Number(viewport?.dataset.richTextBottomReservePx || 0);
        return Number.isFinite(reserve) && reserve > 0 ? reserve : 0;
    }

    function syncViewportSize(viewportElementId) {
        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return;
        }

        // На узких экранах viewport разворачивается в обычный поток страницы.
        if (window.innerWidth <= 1100) {
            viewport.style.maxHeight = "";
            return;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const bottomGap = 16 + getViewportBottomReservePx(viewport);
        const minHeight = 240;
        const availableHeight = Math.max(window.innerHeight - viewportRect.top - bottomGap, minHeight);
        viewport.style.maxHeight = `${availableHeight}px`;
    }

    function registerViewport(viewportElementId) {
        registerVirtualViewport(viewportElementId, null);
    }

    function isScrollNotificationSuppressed(viewportElementId) {
        const entry = registry.get(viewportElementId);
        return !!entry &&
            Number.isFinite(entry.suppressScrollNotificationsUntil) &&
            performance.now() < entry.suppressScrollNotificationsUntil;
    }

    function suppressScrollNotifications(viewportElementId, milliseconds) {
        const entry = registry.get(viewportElementId);
        if (!entry || !Number.isFinite(milliseconds) || milliseconds <= 0) {
            return;
        }

        entry.suppressScrollNotificationsUntil = Math.max(
            entry.suppressScrollNotificationsUntil || 0,
            performance.now() + milliseconds);
    }

    function registerVirtualViewport(viewportElementId, dotNetReference) {
        unregisterViewport(viewportElementId);

        const sync = () => syncViewportSize(viewportElementId);
        let frameId = 0;
        let scrollFrameId = 0;
        let isScrollbarDragging = false;

        const requestSync = () => {
            if (frameId !== 0) {
                return;
            }

            frameId = window.requestAnimationFrame(() => {
                frameId = 0;
                sync();
            });
        };

        const notifyScroll = () => {
            const viewport = document.getElementById(viewportElementId);
            if (!viewport || !dotNetReference) {
                return;
            }

            dotNetReference.invokeMethodAsync(
                "OnVirtualViewportScrolled",
                viewport.scrollTop,
                viewport.clientHeight,
                viewport.scrollHeight);
        };

        const notifyScrollbarReleased = () => {
            const viewport = document.getElementById(viewportElementId);
            if (!viewport || !dotNetReference) {
                return;
            }

            dotNetReference.invokeMethodAsync(
                "OnVirtualViewportScrollbarReleased",
                viewport.scrollTop,
                viewport.clientHeight,
                viewport.scrollHeight);
        };

        const requestScrollNotification = () => {
            if (!dotNetReference ||
                scrollFrameId !== 0 ||
                isScrollbarDragging ||
                isScrollNotificationSuppressed(viewportElementId)) {
                return;
            }

            scrollFrameId = window.requestAnimationFrame(() => {
                scrollFrameId = 0;
                if (isScrollbarDragging || isScrollNotificationSuppressed(viewportElementId)) {
                    return;
                }

                notifyScroll();
            });
        };

        const isVerticalScrollbarPointerEvent = (event) => {
            const viewport = document.getElementById(viewportElementId);
            if (!viewport) {
                return false;
            }

            const rect = viewport.getBoundingClientRect();
            const scrollbarWidth = viewport.offsetWidth - viewport.clientWidth;
            if (scrollbarWidth <= 0) {
                return false;
            }

            return event.clientX >= rect.left + viewport.clientWidth &&
                event.clientX <= rect.right &&
                event.clientY >= rect.top &&
                event.clientY <= rect.bottom;
        };

        const handleMouseDown = (event) => {
            if (!dotNetReference || !isVerticalScrollbarPointerEvent(event)) {
                return;
            }

            isScrollbarDragging = true;
        };

        const handleMouseUp = () => {
            if (!isScrollbarDragging) {
                return;
            }

            isScrollbarDragging = false;
            if (scrollFrameId !== 0) {
                window.cancelAnimationFrame(scrollFrameId);
                scrollFrameId = 0;
            }

            notifyScrollbarReleased();
        };

        const handleKeyDown = (event) => {
            if (event.key !== "PageDown" && event.key !== "PageUp") {
                return;
            }

            window.setTimeout(requestScrollNotification, 0);
        };

        const handleClick = (event) => {
            handleRichTextImageClick(viewportElementId, event);
        };

        const viewport = document.getElementById(viewportElementId);
        if (viewport) {
            viewport.addEventListener("scroll", requestScrollNotification, { passive: true });
            viewport.addEventListener("keydown", handleKeyDown);
            viewport.addEventListener("mousedown", handleMouseDown);
            viewport.addEventListener("click", handleClick);
        }

        registry.set(viewportElementId, {
            requestSync,
            requestScrollNotification,
            handleKeyDown,
            handleMouseDown,
            handleMouseUp,
            handleClick,
            dotNetReference,
            suppressScrollNotificationsUntil: 0
        });
        window.addEventListener("resize", requestSync, { passive: true });
        window.addEventListener("mouseup", handleMouseUp);
        sync();
        measureChunks(viewportElementId);
    }

    function unregisterViewport(viewportElementId) {
        const entry = registry.get(viewportElementId);
        if (!entry) {
            return;
        }

        const viewport = document.getElementById(viewportElementId);
        if (viewport) {
            viewport.removeEventListener("scroll", entry.requestScrollNotification);
            viewport.removeEventListener("keydown", entry.handleKeyDown);
            viewport.removeEventListener("mousedown", entry.handleMouseDown);
            viewport.removeEventListener("click", entry.handleClick);
        }

        window.removeEventListener("resize", entry.requestSync);
        window.removeEventListener("mouseup", entry.handleMouseUp);
        registry.delete(viewportElementId);
        hideImageViewer();
    }

    function handleRichTextImageClick(viewportElementId, event) {
        if (event.defaultPrevented ||
            event.button !== 0 ||
            event.detail !== 1 ||
            event.ctrlKey ||
            event.metaKey ||
            event.shiftKey ||
            event.altKey) {
            return;
        }

        const viewport = document.getElementById(viewportElementId);
        const target = event.target instanceof Element ? event.target : null;
        const image = target?.closest("p.rich-text-image img, img[data-rich-image-id]");
        if (!viewport || !image || !viewport.contains(image)) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        showImageViewer(image);
    }

    function showImageViewer(sourceImage) {
        hideImageViewer();

        const overlay = document.createElement("div");
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-modal", "true");
        overlay.style.position = "fixed";
        overlay.style.inset = "0";
        overlay.style.zIndex = "20000";
        overlay.style.display = "flex";
        overlay.style.alignItems = "center";
        overlay.style.justifyContent = "center";
        overlay.style.padding = "24px";
        overlay.style.background = "rgba(9, 15, 24, 0.78)";
        overlay.style.boxSizing = "border-box";

        const frame = document.createElement("div");
        frame.style.position = "relative";
        frame.style.maxWidth = "96vw";
        frame.style.maxHeight = "92vh";
        frame.style.display = "flex";
        frame.style.alignItems = "center";
        frame.style.justifyContent = "center";

        const image = document.createElement("img");
        image.src = sourceImage.currentSrc || sourceImage.src;
        image.alt = sourceImage.getAttribute("alt") || "";
        image.style.maxWidth = "96vw";
        image.style.maxHeight = "92vh";
        image.style.width = "auto";
        image.style.height = "auto";
        image.style.display = "block";
        image.style.background = "#fff";
        image.style.boxShadow = "0 18px 48px rgba(0, 0, 0, 0.35)";

        const closeButton = document.createElement("button");
        closeButton.type = "button";
        closeButton.setAttribute("aria-label", "Закрыть");
        closeButton.textContent = "x";
        closeButton.style.position = "absolute";
        closeButton.style.top = "-18px";
        closeButton.style.right = "-18px";
        closeButton.style.width = "36px";
        closeButton.style.height = "36px";
        closeButton.style.border = "1px solid rgba(255, 255, 255, 0.65)";
        closeButton.style.borderRadius = "50%";
        closeButton.style.background = "#ffffff";
        closeButton.style.color = "#102033";
        closeButton.style.font = "20px/1 Arial, sans-serif";
        closeButton.style.cursor = "pointer";
        closeButton.style.boxShadow = "0 8px 18px rgba(0, 0, 0, 0.25)";

        frame.appendChild(image);
        frame.appendChild(closeButton);
        overlay.appendChild(frame);
        document.body.appendChild(overlay);

        const handleKeyDown = (event) => {
            if (event.key === "Escape") {
                hideImageViewer();
            }
        };

        const handleMouseDown = (event) => {
            if (event.target === overlay) {
                hideImageViewer();
            }
        };

        closeButton.addEventListener("click", hideImageViewer);
        overlay.addEventListener("mousedown", handleMouseDown);
        document.addEventListener("keydown", handleKeyDown, true);

        activeImageViewer = {
            overlay,
            closeButton,
            handleKeyDown,
            handleMouseDown
        };

        closeButton.focus();
    }

    function hideImageViewer() {
        if (!activeImageViewer) {
            return;
        }

        const viewer = activeImageViewer;
        activeImageViewer = null;
        viewer.closeButton.removeEventListener("click", hideImageViewer);
        viewer.overlay.removeEventListener("mousedown", viewer.handleMouseDown);
        document.removeEventListener("keydown", viewer.handleKeyDown, true);
        viewer.overlay.remove();
    }

    function getChunkBlocks(chunk) {
        const editorHost = chunk.querySelector("[data-rich-text-editor-host]");
        if (editorHost) {
            const editorRoot = editorHost.querySelector(".ProseMirror");
            return Array.from((editorRoot || editorHost).children)
                .filter(element => element.matches(blockSelector));
        }

        return Array.from(chunk.children)
            .filter(element => element.matches(blockSelector));
    }

    function annotateChunkBlocks(viewportElementId) {
        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return;
        }

        viewport.querySelectorAll("[data-rich-text-chunk]").forEach(chunk => {
            getChunkBlocks(chunk).forEach((block, index) => {
                block.setAttribute("data-rich-text-block-index", String(index));
            });
        });
    }

    function getCurrentViewportPosition(viewportElementId) {
        annotateChunkBlocks(viewportElementId);

        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return null;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const anchorY = viewportRect.top + viewportRect.height * 0.30;
        const blocks = [];

        viewport.querySelectorAll("[data-rich-text-chunk]").forEach(chunk => {
            const sortOrder = Number(chunk.getAttribute("data-chunk-sort-order"));
            if (!Number.isFinite(sortOrder)) {
                return;
            }

            getChunkBlocks(chunk).forEach((block, index) => {
                const rect = block.getBoundingClientRect();
                blocks.push({
                    chunkSortOrder: sortOrder,
                    blockIndex: index,
                    rect
                });
            });
        });

        if (blocks.length === 0) {
            return null;
        }

        const visible = blocks
            .filter(item => item.rect.bottom >= viewportRect.top && item.rect.top <= viewportRect.bottom)
            .sort((left, right) =>
                Math.abs((left.rect.top + left.rect.bottom) / 2 - anchorY) -
                Math.abs((right.rect.top + right.rect.bottom) / 2 - anchorY));

        const selected = visible.length > 0 ? visible[0] : null;
        if (!selected) {
            return null;
        }

        return {
            chunkSortOrder: selected.chunkSortOrder,
            blockIndex: selected.blockIndex
        };
    }

    function getCurrentTextSelection(viewportElementId) {
        annotateChunkBlocks(viewportElementId);

        const viewport = document.getElementById(viewportElementId);
        const selection = window.getSelection();
        if (!viewport || !selection || selection.rangeCount === 0) {
            return null;
        }

        const text = selection.toString();
        if (!text || text.trim().length === 0) {
            return null;
        }

        const range = selection.getRangeAt(0);
        if (!viewport.contains(range.commonAncestorContainer)) {
            return null;
        }

        const element = range.commonAncestorContainer.nodeType === Node.ELEMENT_NODE
            ? range.commonAncestorContainer
            : range.commonAncestorContainer.parentElement;
        const chunk = element ? element.closest("[data-rich-text-chunk]") : null;
        if (!chunk || !viewport.contains(chunk)) {
            return null;
        }

        const sortOrder = Number(chunk.getAttribute("data-chunk-sort-order"));
        if (!Number.isFinite(sortOrder)) {
            return null;
        }

        const blocks = getChunkBlocks(chunk);
        let blockIndex = 0;
        const selectedBlock = element ? element.closest(blockSelector) : null;
        if (selectedBlock) {
            const foundIndex = blocks.indexOf(selectedBlock);
            if (foundIndex >= 0) {
                blockIndex = foundIndex;
            }
        }

        return {
            text,
            position: {
                chunkSortOrder: sortOrder,
                blockIndex
            }
        };
    }

    function scrollToBlock(viewportElementId, chunkSortOrder, blockIndex, behavior = "auto", suppressScrollMs = 0) {
        annotateChunkBlocks(viewportElementId);

        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return false;
        }

        const sortOrder = Number(chunkSortOrder);
        const normalizedBlockIndex = Number(blockIndex);
        if (!Number.isFinite(sortOrder) || !Number.isFinite(normalizedBlockIndex)) {
            return false;
        }

        const chunk = viewport.querySelector(`[data-rich-text-chunk][data-chunk-sort-order="${sortOrder}"]`);
        if (!chunk) {
            return false;
        }

        const target = getChunkBlocks(chunk)[normalizedBlockIndex];
        if (!target) {
            return false;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();
        const nextScrollTop = viewport.scrollTop + (targetRect.top - viewportRect.top) - 12;

        suppressScrollNotifications(viewportElementId, suppressScrollMs);
        viewport.scrollTo({
            top: Math.max(nextScrollTop, 0),
            behavior: behavior || "auto",
        });

        return true;
    }

    function scrollToHeading(viewportElementId, headingId, behavior = "smooth", suppressScrollMs = 0) {
        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return false;
        }

        const target = document.getElementById(headingId);
        if (!target) {
            return false;
        }

        if (!viewport.contains(target)) {
            return false;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();
        const nextScrollTop = viewport.scrollTop + (targetRect.top - viewportRect.top) - 12;

        suppressScrollNotifications(viewportElementId, suppressScrollMs);
        viewport.scrollTo({
            top: Math.max(nextScrollTop, 0),
            behavior: behavior || "auto",
        });

        return true;
    }

    function scrollToChunk(viewportElementId, sortOrder, behavior = "auto", suppressScrollMs = 0) {
        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return false;
        }

        const target = viewport.querySelector(`[data-rich-text-chunk][data-chunk-sort-order="${sortOrder}"]`);
        if (!target) {
            return false;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();
        const nextScrollTop = viewport.scrollTop + (targetRect.top - viewportRect.top) - 12;

        suppressScrollNotifications(viewportElementId, suppressScrollMs);
        viewport.scrollTo({
            top: Math.max(nextScrollTop, 0),
            behavior: behavior || "auto",
        });

        return true;
    }

    function ensureChunkVisible(viewportElementId, sortOrder, behavior = "auto", suppressScrollMs = 0) {
        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return false;
        }

        const target = viewport.querySelector(`[data-rich-text-chunk][data-chunk-sort-order="${sortOrder}"]`);
        if (!target) {
            return false;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();
        const topLimit = viewportRect.top + 8;
        const bottomLimit = viewportRect.bottom - 8;

        if (targetRect.bottom < topLimit || targetRect.top > bottomLimit) {
            return scrollToChunk(viewportElementId, sortOrder, behavior, suppressScrollMs);
        }

        return true;
    }

    function getCurrentChunkSortOrder(viewportElementId) {
        const viewport = document.getElementById(viewportElementId);
        if (!viewport) {
            return null;
        }

        const chunks = Array.from(viewport.querySelectorAll("[data-rich-text-chunk]"))
            .map((element) => {
                const sortOrder = Number(element.getAttribute("data-chunk-sort-order"));
                return {
                    element,
                    sortOrder,
                    rect: element.getBoundingClientRect()
                };
            })
            .filter((item) => Number.isFinite(item.sortOrder));

        if (chunks.length === 0) {
            return null;
        }

        const viewportRect = viewport.getBoundingClientRect();
        const anchorY = viewportRect.top + viewportRect.height * 0.35;

        const intersecting = chunks
            .filter((item) => item.rect.bottom >= viewportRect.top && item.rect.top <= viewportRect.bottom)
            .sort((left, right) =>
                Math.abs((left.rect.top + left.rect.bottom) / 2 - anchorY) -
                Math.abs((right.rect.top + right.rect.bottom) / 2 - anchorY));

        if (intersecting.length > 0) {
            return intersecting[0].sortOrder;
        }

        chunks.sort((left, right) =>
            Math.abs((left.rect.top + left.rect.bottom) / 2 - anchorY) -
            Math.abs((right.rect.top + right.rect.bottom) / 2 - anchorY));

        return chunks[0].sortOrder;
    }

    function measureChunks(viewportElementId) {
        const viewport = document.getElementById(viewportElementId);
        const entry = registry.get(viewportElementId);
        if (!viewport || !entry || !entry.dotNetReference) {
            return;
        }

        annotateChunkBlocks(viewportElementId);

        const measurements = Array.from(viewport.querySelectorAll("[data-rich-text-chunk]"))
            .map((element) => ({
                sortOrder: Number(element.getAttribute("data-chunk-sort-order")),
                height: element.getBoundingClientRect().height
            }))
            .filter((item) => Number.isFinite(item.sortOrder) && Number.isFinite(item.height) && item.height > 0);

        if (measurements.length === 0) {
            return;
        }

        entry.dotNetReference.invokeMethodAsync("OnChunkHeightsMeasured", measurements);
    }

    return {
        syncViewportSize,
        registerViewport,
        registerVirtualViewport,
        unregisterViewport,
        scrollToHeading,
        scrollToChunk,
        ensureChunkVisible,
        suppressScrollNotifications,
        getCurrentViewportPosition,
        getCurrentTextSelection,
        scrollToBlock,
        getCurrentChunkSortOrder,
        measureChunks
    };
}

window.richTextReadViewport = createRichTextViewportRuntime();
window.richTextEditViewport = createRichTextViewportRuntime();
window.richTextOutlineViewport = createRichTextViewportRuntime();

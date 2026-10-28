window.TreeMultiSelect = {
    // Принудительное обновление CSS-классов дерева
    forceRefreshTreeSelection: function () {
        try {
            // Находим все элементы с классом tree-node-selected
            const selectedElements = document.querySelectorAll('.rz-treenode-content-selected');
            console.log(`Found ${selectedElements.length} elements with rz-treenode-content-selected`);
            
            // Принудительно удаляем класс выделения
            selectedElements.forEach((element, index) => {
                console.log(`Removing rz-treenode-content-selected class from element ${index}: ${element.textContent}`);
                element.classList.remove('rz-treenode-content-selected');
                
                // Принудительно обновляем стили
                element.style.backgroundColor = '';
                element.style.color = '';
            });
            
            // Принудительно перерисовываем DOM
            document.body.offsetHeight; // Trigger reflow
            
            return selectedElements.length;
        } catch (error) {
            console.error('Error in forceRefreshTreeSelection:', error);
            return -1;
        }
    },
    
    // Переменная для хранения ссылки на всплывающий элемент
    dragTooltip: null,
    
    // Обработчик движения мыши для обновления позиции всплывающего элемента
    onMouseMove: function(e) {
        if (this.dragTooltip) {
            this.dragTooltip.style.left = (e.clientX + 10) + 'px';
            this.dragTooltip.style.top = (e.clientY + 10) + 'px';
        }
    }
};

window.TreeNodeTooltip = {
    tooltip: null,
    showTimer: null,
    lastClientX: 0,
    lastClientY: 0,
    moveHandler: null,

    show: function (content, clientX, clientY) {
        this.hide();

        if (!content) {
            return;
        }

        this.lastClientX = clientX || 0;
        this.lastClientY = clientY || 0;
        this.moveHandler = (event) => {
            this.lastClientX = event.clientX;
            this.lastClientY = event.clientY;
            this.position();
        };

        document.addEventListener('mousemove', this.moveHandler);
        this.showTimer = window.setTimeout(() => {
            const tooltip = document.createElement('div');
            tooltip.className = 'tree-node-full-name-tooltip';
            tooltip.textContent = content;
            tooltip.style.cssText = `
                position: fixed;
                z-index: 10001;
                max-width: 420px;
                padding: 6px 9px;
                border-radius: 4px;
                background: rgba(17, 24, 39, 0.96);
                color: #ffffff;
                font-size: 12px;
                line-height: 1.3;
                pointer-events: none;
                box-shadow: 0 4px 14px rgba(0, 0, 0, 0.24);
                white-space: normal;
                overflow-wrap: anywhere;
            `;

            document.body.appendChild(tooltip);
            this.tooltip = tooltip;
            this.position();
        }, 1000);
    },

    position: function () {
        if (!this.tooltip) {
            return;
        }

        const offset = 12;
        const margin = 8;
        let left = this.lastClientX + offset;
        let top = this.lastClientY + offset;
        const rect = this.tooltip.getBoundingClientRect();

        if (left + rect.width + margin > window.innerWidth) {
            left = Math.max(margin, this.lastClientX - rect.width - offset);
        }

        if (top + rect.height + margin > window.innerHeight) {
            top = Math.max(margin, this.lastClientY - rect.height - offset);
        }

        this.tooltip.style.left = `${left}px`;
        this.tooltip.style.top = `${top}px`;
    },

    hide: function () {
        if (this.showTimer) {
            window.clearTimeout(this.showTimer);
            this.showTimer = null;
        }

        if (this.tooltip) {
            this.tooltip.remove();
            this.tooltip = null;
        }

        if (this.moveHandler) {
            document.removeEventListener('mousemove', this.moveHandler);
            this.moveHandler = null;
        }
    }
};

// Функция для создания всплывающего элемента с именами перетаскиваемых элементов
window.createDragTooltip = function(content) {
    try {
        // Удаляем существующий tooltip, если есть
        window.removeDragTooltip();
        
        // Создаем новый элемент
        const tooltip = document.createElement('div');
        tooltip.id = 'drag-tooltip';
        tooltip.innerHTML = content;
        tooltip.style.cssText = `
            position: fixed;
            background: rgba(0, 0, 0, 0.8);
            color: white;
            padding: 8px 12px;
            border-radius: 4px;
            font-size: 12px;
            max-width: 200px;
            z-index: 10000;
            pointer-events: none;
            white-space: nowrap;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
        `;
        
        document.body.appendChild(tooltip);
        window.TreeMultiSelect.dragTooltip = tooltip;
        
        // Добавляем обработчик движения мыши
        document.addEventListener('mousemove', window.TreeMultiSelect.onMouseMove);
        
        console.log('Drag tooltip created with content:', content);
    } catch (error) {
        console.error('Error creating drag tooltip:', error);
    }
};

// Функция для обновления позиции всплывающего элемента
window.updateDragTooltipPosition = function(clientX, clientY) {
    try {
        const tooltip = window.TreeMultiSelect.dragTooltip;
        if (tooltip) {
            tooltip.style.left = (clientX + 10) + 'px';
            tooltip.style.top = (clientY + 10) + 'px';
        }
    } catch (error) {
        console.error('Error updating drag tooltip position:', error);
    }
};

// Функция для удаления всплывающего элемента
window.removeDragTooltip = function() {
    try {
        const tooltip = document.getElementById('drag-tooltip');
        if (tooltip) {
            tooltip.remove();
        }
        
        // Очищаем ссылку
        window.TreeMultiSelect.dragTooltip = null;
        
        // Удаляем обработчик движения мыши
        document.removeEventListener('mousemove', window.TreeMultiSelect.onMouseMove);
        
        console.log('Drag tooltip removed');
    } catch (error) {
        console.error('Error removing drag tooltip:', error);
    }
};

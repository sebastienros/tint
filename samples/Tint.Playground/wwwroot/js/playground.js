// The editor half of the playground: a transparent textarea over the highlighted <pre> (the shiki playground trick),
// kept in sync without Blazor re-rendering either of them.

const debounceMs = 50;
const hashDebounceMs = 400;

export function readHash() {
    return location.hash;
}

export function writeHash(hash) {
    history.replaceState(null, '', '#' + hash);
}

export function copyText(text) {
    return navigator.clipboard.writeText(text);
}

export function setColors(background, foreground) {
    document.documentElement.style.setProperty('--page-bg', background);
    document.documentElement.style.setProperty('--page-fg', foreground);
}

export function markInteractive() {
    const measure = performance.measure('tint-interactive', 'tint-start');
    console.info(`Tint playground interactive in ${measure.duration.toFixed(0)} ms`);
    return measure.duration;
}

export function init(textarea, output, dotnet, code, html) {
    const editor = { textarea, output, dotnet, timer: 0, hashTimer: 0, hovering: false, lastHover: null, frame: 0 };
    textarea.value = code;
    output.innerHTML = html;

    textarea.addEventListener('input', () => {
        clearTimeout(editor.timer);
        editor.timer = setTimeout(() => update(editor), debounceMs);
        clearTimeout(editor.hashTimer);
        editor.hashTimer = setTimeout(() => dotnet.invokeMethodAsync('WriteHash'), hashDebounceMs);
    });

    textarea.addEventListener('scroll', () => syncScroll(editor));

    // Tab inserts a tab instead of leaving the editor; Escape then Tab leaves it.
    textarea.addEventListener('keydown', event => {
        if (event.key === 'Tab' && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey && !editor.escaped) {
            event.preventDefault();
            document.execCommand('insertText', false, '\t');
        }

        editor.escaped = event.key === 'Escape';
    });

    const caret = () => {
        if (!editor.hovering) {
            inspectCaret(editor);
        }
    };
    textarea.addEventListener('keyup', caret);
    textarea.addEventListener('click', caret);
    textarea.addEventListener('select', caret);
    textarea.addEventListener('focus', caret);

    textarea.addEventListener('mousemove', event => {
        const x = event.clientX;
        const y = event.clientY;
        cancelAnimationFrame(editor.frame);
        editor.frame = requestAnimationFrame(() => hover(editor, x, y));
    });

    textarea.addEventListener('mouseleave', () => {
        cancelAnimationFrame(editor.frame);
        editor.hovering = false;
        editor.lastHover = null;
        inspectCaret(editor);
    });

    return {
        setCode(code, html) {
            textarea.value = code;
            output.innerHTML = html;
            syncScroll(editor);
        },
        setHtml(html) {
            output.innerHTML = html;
            syncScroll(editor);
        },
        getCode() {
            return textarea.value;
        },
    };
}

function update(editor) {
    const html = editor.dotnet.invokeMethod('Update', editor.textarea.value);
    editor.output.innerHTML = html;
    syncScroll(editor);
    if (!editor.hovering) {
        inspectCaret(editor);
    }
}

function syncScroll(editor) {
    const pre = editor.output.firstElementChild;
    if (pre) {
        pre.scrollTop = editor.textarea.scrollTop;
        pre.scrollLeft = editor.textarea.scrollLeft;
    }
}

function inspectCaret(editor) {
    const offset = editor.textarea.selectionDirection === 'backward' ? editor.textarea.selectionStart : editor.textarea.selectionEnd;
    editor.dotnet.invokeMethod('InspectCaret', offset);
}

// The character under the mouse, found in the <pre> under the textarea: its line (the index of its span.line) and its
// column (the text before it in that line).
function hover(editor, x, y) {
    const position = characterAt(editor, x, y);
    editor.hovering = position !== null;
    const key = position ? position.line + ':' + position.column : null;
    if (key === editor.lastHover) {
        return;
    }

    editor.lastHover = key;
    if (position) {
        editor.dotnet.invokeMethod('InspectAt', position.line, position.column);
    } else {
        inspectCaret(editor);
    }
}

function characterAt(editor, x, y) {
    editor.textarea.style.pointerEvents = 'none';
    let node = null;
    let offset = 0;
    try {
        if (document.caretPositionFromPoint) {
            const position = document.caretPositionFromPoint(x, y);
            if (position) {
                node = position.offsetNode;
                offset = position.offset;
            }
        } else if (document.caretRangeFromPoint) {
            const range = document.caretRangeFromPoint(x, y);
            if (range) {
                node = range.startContainer;
                offset = range.startOffset;
            }
        }
    } finally {
        editor.textarea.style.pointerEvents = '';
    }

    if (!node || node.nodeType !== Node.TEXT_NODE || !editor.output.contains(node)) {
        return null;
    }

    // The caret position is the gap nearest the point: the character is the one after it or the one before it.
    const range = document.createRange();
    const contains = (start) => {
        if (start < 0 || start >= node.length) {
            return false;
        }

        range.setStart(node, start);
        range.setEnd(node, start + 1);
        const rect = range.getBoundingClientRect();
        return x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
    };

    let character = contains(offset) ? offset : contains(offset - 1) ? offset - 1 : -1;
    if (character < 0) {
        return null;
    }

    const line = node.parentElement.closest('.line');
    if (!line) {
        return null;
    }

    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
    let column = 0;
    for (let text = walker.nextNode(); text && text !== node; text = walker.nextNode()) {
        column += text.length;
    }

    const lines = line.parentElement.children;
    return { line: Array.prototype.indexOf.call(lines, line), column: column + character };
}

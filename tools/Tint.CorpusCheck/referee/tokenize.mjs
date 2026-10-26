// Tokenizes a file with vscode-textmate over vscode-oniguruma, the reference behavior Tint ports, using the checkout's
// grammars/ (injections from grammars/manifest.json) and Dark+ (themes/theme-defaults/dark_plus.json with its include
// flattened, read as written). Prints one block per line: "L<n>", then "start-end scopes" per token of tokenizeLine,
// ends clamped to the line and empty tokens dropped as Tint.CorpusCheck normalizes them, then "S start foreground
// background fontStyle" per token of tokenizeLine2. Without the S lines, the output diffs against
// `Tint.CorpusCheck dump`.
//
//   node tokenize.mjs <repo-root> <scope> <file> [first-line] [last-line]
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import vsctm from 'vscode-textmate';
import oniguruma from 'vscode-oniguruma';

const require = createRequire(import.meta.url);
const [root, scope, file, first = '1', last = '1000000000'] = process.argv.slice(2);
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'grammars', 'manifest.json'), 'utf8'));
const byScope = new Map(manifest.grammars.map(g => [g.scopeName, g]));

const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')).buffer;
await oniguruma.loadWASM(wasm);

const registry = new vsctm.Registry({
    onigLib: Promise.resolve({
        createOnigScanner: patterns => new oniguruma.OnigScanner(patterns),
        createOnigString: s => new oniguruma.OnigString(s),
    }),
    loadGrammar: async scopeName => {
        const grammar = byScope.get(scopeName);
        if (!grammar) {
            return null;
        }
        const grammarPath = path.join(root, 'grammars', grammar.path);
        return vsctm.parseRawGrammar(fs.readFileSync(grammarPath, 'utf8'), grammarPath);
    },
    getInjections: scopeName => manifest.grammars.filter(g => (g.injectTo ?? []).includes(scopeName)).map(g => g.scopeName),
});

// VS Code's theme files are JSON with comments and trailing commas.
function readJsonc(file) {
    const text = fs.readFileSync(file, 'utf8');
    let out = '';
    for (let i = 0, inString = false; i < text.length; i++) {
        const c = text[i];
        if (inString) {
            out += c;
            if (c === '\\') {
                out += text[++i];
            } else if (c === '"') {
                inString = false;
            }
        } else if (c === '"') {
            inString = true;
            out += c;
        } else if (c === '/' && text[i + 1] === '/') {
            while (i < text.length && text[i] !== '\n') i++;
            out += '\n';
        } else if (c === '/' && text[i + 1] === '*') {
            i = text.indexOf('*/', i + 2) + 1;
        } else {
            out += c;
        }
    }
    return JSON.parse(out.replace(/,(\s*[\]}])/g, '$1'));
}

function themeRules(file) {
    const theme = readJsonc(file);
    const included = theme.include ? themeRules(path.join(path.dirname(file), theme.include)) : [];
    return [...included, ...(theme.tokenColors ?? theme.settings ?? [])];
}

registry.setTheme({ name: 'Dark+', settings: themeRules(path.join(root, 'themes', 'theme-defaults', 'dark_plus.json')) });
const colorMap = registry.getColorMap();
const grammar = await registry.loadGrammar(scope);
// A UTF-8 byte order mark is not text, as .NET's File.ReadAllText and editors treat it.
const lines = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '').split(/\r\n|\r|\n/);
if (lines.length > 0 && lines[lines.length - 1] === '') {
    lines.pop();
}

let state = vsctm.INITIAL;
const out = [];
for (let i = 0; i < lines.length && i < Number(last); i++) {
    const line = lines[i];
    const result = grammar.tokenizeLine(line, state);
    const binary = grammar.tokenizeLine2(line, state).tokens;
    state = result.ruleStack;
    if (i + 1 < Number(first)) {
        continue;
    }
    out.push(`L${i + 1}`);
    let kept = 0;
    for (const token of result.tokens) {
        const end = Math.min(token.endIndex, line.length);
        if (end > token.startIndex || (line.length === 0 && kept === 0)) {
            out.push(`${token.startIndex}-${end} ${token.scopes.join(' ')}`);
            kept++;
        }
    }
    for (let t = 0; t < binary.length; t += 2) {
        const metadata = binary[t + 1];
        const foreground = (metadata & 0b0000_0000_1111_1111_1000_0000_0000_0000) >>> 15;
        const background = (metadata & 0b1111_1111_0000_0000_0000_0000_0000_0000) >>> 24;
        const fontStyle = (metadata & 0b0000_0000_0000_0000_0111_1000_0000_0000) >>> 11;
        out.push(`S ${binary[t]} ${colorMap[foreground] ?? ''} ${colorMap[background] ?? ''} ${fontStyle}`);
    }
}
process.stdout.write(out.join('\n') + '\n');

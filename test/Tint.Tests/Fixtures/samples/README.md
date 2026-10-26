# Language samples

Sample files from [shikijs/textmate-grammars-themes](https://github.com/shikijs/textmate-grammars-themes)
(`samples/<id>.sample`, MIT License: Copyright (c) 2021 Pine Wu, Copyright (c) 2023 Anthony Fu), commit
[`8f1828ebccc57930cf0adde9b35ae929584fe191`](https://github.com/shikijs/textmate-grammars-themes/tree/8f1828ebccc57930cf0adde9b35ae929584fe191/samples).

`BundleTests` tokenizes each bundled grammar's sample with the generated class and with the loader path and checks they
agree. Files are named by the VS Code language id; where shiki uses another id the sample was renamed:

| Language id | shiki sample |
|---|---|
| `coffeescript` | `coffee` |
| `dockerfile` | `docker` |
| `jade` | `pug` |
| `javascriptreact` | `jsx` |
| `makefile` | `make` |
| `restructuredtext` | `rst` |
| `typescriptreact` | `tsx` |

shiki has no sample for `cpp_embedded_latex`, `cuda-cpp`, `git-commit`, `git-rebase`, `ignore`, `markdown-math`,
`markdown_latex_combined`, `prompt`, `search-result` and `snippets`, nor for the grammars without a language
(injections, `source.c.platform`, `source.js.regexp`, `source.regexp.python`, `source.sassdoc`, `source.yaml.1.x`,
`source.yaml.embedded`, `text.html.basic`); those grammars tokenize their own grammar file instead.

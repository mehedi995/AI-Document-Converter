# Third-Party Notices

AI Document Converter incorporates third-party software. This file lists every component
distributed with the product, its version, its licence, and the attribution its licence requires.

**Last verified:** 2026-09-08, against the built engine bundle at
`src/AI.Document.Converter.Python/dist/AIDocumentConverter.PythonEngine/` and the `PackageReference`
entries in `src/*/*.csproj`. Licences were read from installed package metadata (`importlib.metadata`
and NuGet `.nuspec`), not from memory or documentation.

Regenerate the inventory with:

```bash
python scripts/check-licences.py
```

---

## 1. Licence policy

Every component distributed with this product must be permissively licensed.

**Nothing under AGPL may ever ship.** The cloud edition serves document conversion over a network,
which triggers AGPL §13's obligation to offer the complete corresponding source of the combined
work. Running the component in a subprocess or a separate container does **not** discharge that
obligation.

**PyMuPDF was removed for exactly this reason.** It is dual-licensed AGPL-3.0 / Artifex commercial
and was replaced by pdfplumber (MIT) in 2026-09. See `docs/saas/02-PDF-ENGINE-BENCHMARK.md`.
PyMuPDF remains a **development-only** dependency (`requirements-dev.txt`) for fixture generation
and benchmark comparison; it is neither distributed nor served over a network, so §13 is not
engaged. `scripts/check-licences.py` fails the build if it ever appears in a shipped bundle.

---

## 2. Components requiring particular attention

| Component | Licence | Why it is called out |
|---|---|---|
| **certifi** | **MPL-2.0** | The only weak-copyleft component that ships. MPL-2.0 permits distribution inside a proprietary product, but obligations attach **per file**: if any certifi file is modified, the source of that file must be made available. We do not modify certifi. Its source is available at the URL below. |
| **regex** | Apache-2.0 **AND** CNRI-Python | Dual obligation, both permissive. CNRI-Python requires its notice be retained (reproduced below). |
| **lxml** | BSD-3-Clause | Distributes bundled **libxml2** and **libxslt** (both MIT), which carry their own notices. |
| **pypdfium2** | BSD-3-Clause / Apache-2.0 | Embeds Google **PDFium** (BSD-3-Clause), which itself vendors further components; see the licence files shipped inside the package. |
| **OpenSSL** (`libcrypto-3.dll`, `libssl-3.dll`) | Apache-2.0 | Ships via CPython. |
| **Microsoft Visual C++ Runtime** (`VCRUNTIME140*.dll`) | Microsoft redistributable licence | Redistributed under the Visual Studio redistributable terms. |

### Known bundle-hygiene issue

**numpy (BSD-3-Clause) and pandas (BSD-3-Clause) are present in the built bundle although no source
file in this project imports them.** PyInstaller pulls them in through an optional import chain
(openpyxl can use numpy when available). They are permissively licensed, so this is not a licence
defect, but it is unnecessary distribution surface and bundle weight. Tracked as a cleanup task in
`IMPLEMENTATION_STATUS.md`; both are listed below because they *are* currently distributed, and this
file must describe what actually ships rather than what ideally would.

---

## 3. Python engine — distributed components

Bundled into `AIDocumentConverter.PythonEngine` and shipped with the product.

| Component | Version | Licence | Project |
|---|---|---|---|
| CPython runtime | 3.13 | PSF-2.0 | https://www.python.org/ |
| pdfplumber | 0.11.10 | MIT | https://github.com/jsvine/pdfplumber |
| pdfminer.six | 20260107 | MIT | https://github.com/pdfminer/pdfminer.six |
| pypdfium2 | 5.13.0 | BSD-3-Clause / Apache-2.0 | https://github.com/pypdfium2-team/pypdfium2 |
| Pillow | 12.3.0 | MIT-CMU (HPND) | https://python-pillow.github.io |
| python-docx | 1.2.0 | MIT | https://github.com/python-openxml/python-docx |
| openpyxl | 3.1.5 | MIT | https://openpyxl.readthedocs.io |
| et-xmlfile | 2.0.0 | MIT | https://foss.heptapod.net/openpyxl/et_xmlfile |
| python-pptx | 1.0.2 | MIT | https://github.com/scanny/python-pptx |
| XlsxWriter | 3.2.9 | BSD-2-Clause | https://github.com/jmcnamara/XlsxWriter |
| lxml | 6.1.2 | BSD-3-Clause (bundles libxml2, libxslt — MIT) | https://lxml.de/ |
| tiktoken | 0.14.0 | MIT | https://github.com/openai/tiktoken |
| regex | 2026.7.19 | Apache-2.0 AND CNRI-Python | https://github.com/mrabarnett/mrab-regex |
| cryptography | 50.0.1 | Apache-2.0 OR BSD-3-Clause | https://github.com/pyca/cryptography |
| charset-normalizer | 3.5.1 | MIT | https://github.com/jawah/charset_normalizer |
| certifi | 2026.7.22 | **MPL-2.0** | https://github.com/certifi/python-certifi |
| numpy | 2.2.3 | BSD-3-Clause | https://numpy.org/ |
| pandas | 2.2.3 | BSD-3-Clause | https://pandas.pydata.org/ |
| python-dateutil | 2.9.0.post0 | Apache-2.0 / BSD-3-Clause | https://github.com/dateutil/dateutil |
| pytz | 2025.1 | MIT | https://pythonhosted.org/pytz/ |
| setuptools | 84.0.0 | MIT | https://github.com/pypa/setuptools |
| typing-extensions | 4.16.0 | PSF-2.0 | https://github.com/python/typing_extensions |
| OpenSSL | 3.x | Apache-2.0 | https://www.openssl.org/ |

### tiktoken vocabulary data

`src/AI.Document.Converter.Python/tiktoken_cache/` contains the `o200k_base` and `cl100k_base`
byte-pair-encoding vocabulary files, redistributed from OpenAI's tiktoken project (MIT). They are
bundled deliberately so the engine never makes a network call to fetch them
(see `tokenizer.py`).

---

## 4. .NET application — distributed components

| Component | Version | Licence | Copyright |
|---|---|---|---|
| Microsoft.Extensions.Configuration | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.Configuration.Binder | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.Configuration.Json | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.Logging.Abstractions | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.Options | 10.0.11 | MIT | © Microsoft Corporation |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.11 | MIT | © Microsoft Corporation |
| Serilog | 4.4.0 | Apache-2.0 | © Serilog Contributors |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | © Serilog Contributors |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | © Serilog Contributors |
| .NET Runtime / WPF | 8.0 | MIT | © Microsoft Corporation |

---

## 5. Development and test only — NOT distributed

These are build- and test-time tools. They are not part of any shipped artifact and impose no
distribution obligations.

| Component | Version | Licence |
|---|---|---|
| **PyMuPDF** | 1.28.2 | **AGPL-3.0 / Artifex commercial** — dev-only; see §1 |
| PyInstaller | 6.22.2 | GPL-2.0-or-later **with a bootloader exception** permitting proprietary bundling |
| xunit | 2.5.3 | Apache-2.0 |
| xunit.runner.visualstudio | 2.5.3 | Apache-2.0 |
| Moq | 4.20.72 | BSD-3-Clause |
| Microsoft.NET.Test.Sdk | 17.8.0 | MIT |
| coverlet.collector | 6.0.0 | MIT |

> **PyInstaller note.** PyInstaller is GPL-2.0-or-later, but its licence grants a specific exception
> allowing the bootloader it injects into a frozen application to be distributed with software under
> any licence, including proprietary. This project relies on that exception. PyInstaller itself is
> not distributed.

---

## 6. Licence texts

### MIT License

Applies to: pdfplumber, pdfminer.six, python-docx, openpyxl, et-xmlfile, python-pptx, tiktoken,
charset-normalizer, pytz, setuptools, libxml2, libxslt, and all Microsoft.Extensions.* packages,
the .NET runtime, Microsoft.NET.Test.Sdk and coverlet.collector.

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Each package's own copyright notice is retained in the licence file shipped inside that package
within the bundle.

### BSD 3-Clause License

Applies to: lxml, numpy, pandas, python-dateutil, pypdfium2 (BSD portions), PDFium, cryptography
(BSD option), Moq.

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

XlsxWriter is BSD-2-Clause: the same terms without clause 3.

### Apache License 2.0

Applies to: Serilog, Serilog.Extensions.Logging, Serilog.Sinks.File, xunit,
xunit.runner.visualstudio, regex (in part), cryptography (Apache option), python-dateutil (Apache
option), pypdfium2 (Apache portions), OpenSSL.

The full text is available at https://www.apache.org/licenses/LICENSE-2.0 and is reproduced in the
licence files shipped inside each relevant package.

```
Licensed under the Apache License, Version 2.0 (the "License");
you may not use these files except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
License for the specific language governing permissions and limitations under
the License.
```

### Mozilla Public License 2.0 — certifi

The full text is available at https://mozilla.org/MPL/2.0/.

certifi is distributed unmodified. Its complete source is available at
https://github.com/certifi/python-certifi. Should any certifi file ever be modified, the source of
that modified file must be made available under MPL-2.0.

### MIT-CMU (HPND) — Pillow

```
Permission to use, copy, modify and distribute this software and its
documentation for any purpose and without fee is hereby granted, provided that
the above copyright notice appears in all copies, and that both that copyright
notice and this permission notice appear in supporting documentation, and that
the name of the copyright holder not be used in advertising or publicity
pertaining to distribution of the software without specific, written prior
permission.

THE COPYRIGHT HOLDER DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE,
INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT
SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY SPECIAL, INDIRECT OR
CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE
OF THIS SOFTWARE.
```

### CNRI-Python — regex (in part)

The `regex` module derives in part from CPython's `re` module. The CNRI-Python licence requires its
notice be retained; it is reproduced in the licence file shipped inside the `regex` package and at
https://github.com/mrabarnett/mrab-regex.

### Python Software Foundation License 2.0

Applies to: the CPython runtime and typing-extensions. Full text at
https://docs.python.org/3/license.html.

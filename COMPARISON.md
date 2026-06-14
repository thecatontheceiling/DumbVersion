## Comparison with xdelta

Below are comparison tables comparing DumbVersion with [xdelta3](https://archlinux.org/packages/extra/x86_64/xdelta3/). All of the results are captured from an Arch Linux x64 Hyper-V virtual machine running under an Intel i5-9600 CPU with 5 GB of RAM without swap.      
Keep in mind that performance can vary wildly between different operating systems, not to mention different hardware. DumbVersion benefits most from CPU and RAM performance.

### English Windows XP ISO to Japanese

ISO file sizes were:
- en: 589.1 MiB
- ja: 630.2 MiB

| Tool | Action | Time (s) | CPU (%) | Peak RAM (MB)* | Patch Size (MB) |
|------|--------|----------|---------|----------------|-----------------|
| xdelta3 | Create | 122.65 | 98 | 227.26 | 482.09 |
| DumbVersion | Create | 7.72 | 87 | 74.31 | 339.39 |
| xdelta3 | Apply | 1.90 | 65 | 98.21 | - |
| DumbVersion | Apply | 3.78 | 87 | 7.83 | - |

### English Windows 11 25H2 ISO to Finnish

ISO file sizes were:
- en-us: 7.9 GiB
- fi-fi: 7.8 GiB

| Tool | Action | Time (s) | CPU (%) | Peak RAM (MB)* | Patch Size (MB) |
|------|--------|----------|---------|----------------|-----------------|
| xdelta3 | Create | 1502.38 | 97 | 227.28 | 5804.28 |
| DumbVersion | Create | 70.54 | 76 | 557.18 | 336.18 |
| xdelta3 | Apply | 22.26 | 72 | 100.82 | - |
| DumbVersion | Apply | 83.41 | 46 | 7.53 | - |

---

\* *Excludes file-backed page caching (mmap). System monitoring tools by default will also include all input file mappings, which doesn't accurately represent the actual memory footprint.*
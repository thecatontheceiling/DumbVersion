<h1 align="center">DumbVersion</h1>
<p align="center">A high-performance binary diffing and patching engine for massive files.</p>

<hr>

## Description

DumbVersion is a modern, format-agnostic binary diffing tool designed to generate the smallest possible patch files (`.dvp`) between two binary files. Inspired by xdelta and SmartVersion, DumbVersion is specifically optimized for ISOs, disk images, uncompressed ROMs, and large archives.    

Using **Fast Content-Defined Chunking** (FastCDC) and **xxHash3**, DumbVersion identifies shifted, inserted or deleted data chunks without byte-alignment constraints. This enables efficient deduplication and tiny patch sizes even when structural offsets change heavily between versions.

### Features
- Platform and file format agnostic.
- Meticulously optimized to use the least amount of memory and CPU possible.
- Built-in support for rapidly creating patches for entire directories of target files.
- Includes an extremely user-friendly interactive TUI Patcher designed for end-users.
- Specifically designed to be compiled ahead-of-time (AOT), which means there are **zero** extra dependencies, including the .NET runtime.

---

## Usage

DumbVersion is split into two primary executables:
1. `DumbVersionCreator` - The tool used by distributors or hoarders to **generate** patch files.
2. `DumbVersionPatcher` - The tool distributed to end-users to **apply** patch files.

---

### Patch Generation (`DumbVersionCreator`)

The CLI tool is used to index base files, diff them against targets, and emit `.dvp` (DumbVersion Patch) files.

**Single File Mode**    
To create a patch from a single base file to a single target file:
```
DumbVersionCreator <base_file> <target_file> [output.dvp]
```
*(If `output.dvp` is omitted, it will automatically be placed in the target file's directory and be named the same as the target_file).*

**Bulk Mode**   
To create patches for a specific folder of target files derived from the same base:
```
DumbVersionCreator -bulk/--bulk <base_file> <target_folder> [output_folder]
```

**Auto-bulk Mode**   
To pass just a base file to automatically generate patches for all files in the base file's folder that share the exact same extension. Output is placed in a `DVPs` subfolder:
```
DumbVersionCreator <base_file>
```

---

### Patch Application (`DumbVersionPatcher`)

The Patcher is built entirely around end-user safety and speed. All possible complications are handled gracefully with an interactive TUI.

#### Interactive Mode

Most end users can simply drop `DumbVersionPatcher` into a folder alongside their base file and `.dvp` patch file(s) and double-click the executable (or run it with no arguments):
```
DumbVersionPatcher
```
* If multiple patches or base files are found, the patcher will prompt the user with an interactive selection menu.
* You can type `0` to automatically bulk-apply all patches found in the directory.
* You can type `info <number>` to inspect a patch's metadata (hashes, target size, base filename) without applying it.
* Provides a real-time progress bar.
* Prompts for confirmation if the user could be accidentally overwriting existing files.

#### Command-Line Mode
For automated scripts:

**Apply Specific Patches**
```
DumbVersionPatcher [-o/--output output_path] [base_file] [patch1.dvp, patch2.dvp ...]
```
* **`base_file`**: Explicitly specify the base file to patch against. If omitted, the patcher will automatically search the directory for the base file expected by the `.dvp` metadata.

**Apply Directory in Bulk**
```
DumbVersionPatcher -bulk/--bulk <patch_folder> [base_file_or_folder] [-o/--output output_folder]
```
* Bypasses prompts and applies all `.dvp` files found in the target `<patch_folder>`.

**Inspect Patch Info**
```
DumbVersionPatcher -info/-i <patch_file_or_folder>
```
* Reads the patch file headers and prints base hashes, target hashes, file sizes, and expected base filenames. Does not apply the patch.

---

## Building

DumbVersion relies heavily on modern .NET features and is designed around NativeAOT deployment. All OSs require the .NET 8.0 SDK or newer to build the project.
See [this page](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8#prerequisites) for more details on prerequisites.

To build the binaries, run the publish command targeting your specific OS and architecture:

```
dotnet publish -c Release -r <target_os>
```
*(`-c Release` is included as some people may have `Debug` set to default instead.)*

**Tested Target OS RIDs:**
* Windows x64 - `win-x64`
* Linux x64: `linux-x64`

See [this page](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog#known-rids) to find the rest of the possible target RIDs.

Once completed, check the `<project>/bin/Release/net<version>/<target_os>/publish` folder. The resulting executables will be fully self-contained and don't require the user to install the .NET runtime.

## Acknowledgements

Without these two people, I would have had a much harder time finishing this project:

- **[asdcorp](https://github.com/asdcorp)** - Came up with the idea of using FastCDC chunking for better diffing results. Developed a working prototype implementation that I used as a reference during early stages of development.
- **[WitherOrNot](https://github.com/WitherOrNot)** - Co-developer, helped significantly in the late stages of development. Brought the Creator and Patcher to completion. 
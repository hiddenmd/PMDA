/**
 * The files in a drop, including everything inside dropped folders, sorted by path. Hidden files
 * (".DS_Store", "Thumbs.db" style) are skipped. Entries must be taken from the event synchronously;
 * reading them can then happen after the event handler returns.
 */
export async function droppedFiles(transfer: DataTransfer): Promise<File[]> {
  const entries = Array.from(transfer.items).map((i) => i.webkitGetAsEntry?.() ?? null).filter((e): e is FileSystemEntry => e !== null)
  if (entries.length === 0) return visible(Array.from(transfer.files))

  const found: { path: string; file: File }[] = []
  const walk = async (entry: FileSystemEntry): Promise<void> => {
    if (entry.name.startsWith('.')) return
    if (entry.isFile) {
      const file = await new Promise<File>((resolve, reject) => (entry as FileSystemFileEntry).file(resolve, reject))
      if (file.size > 0) found.push({ path: entry.fullPath, file })
    } else if (entry.isDirectory) {
      const reader = (entry as FileSystemDirectoryEntry).createReader()
      // readEntries returns at most ~100 entries per call; call until it returns none.
      for (;;) {
        const batch = await new Promise<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject))
        if (batch.length === 0) break
        for (const child of batch) await walk(child)
      }
    }
  }
  for (const entry of entries) await walk(entry)
  return found.sort((a, b) => a.path.localeCompare(b.path, undefined, { numeric: true })).map((f) => f.file)
}

/** Files from a file input (several files, or a whole folder), hidden files skipped, sorted by path. */
export function pickedFiles(list: FileList | null): File[] {
  const files = visible(list ? Array.from(list) : [])
  const path = (f: File) => f.webkitRelativePath || f.name
  return files.sort((a, b) => path(a).localeCompare(path(b), undefined, { numeric: true }))
}

const visible = (files: File[]) => files.filter((f) => !f.name.startsWith('.') && f.size > 0)

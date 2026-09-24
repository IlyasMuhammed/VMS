/**
 * What the browser can check about a file before it is sent, in the same words as the server (which decides
 * from the file's real bytes, and has the last say): kind by name and type, size, not empty. No framework imports;
 * tested under Node.
 */

export type FileKind = 'pdf' | 'png' | 'jpeg' | 'docx' | 'xlsx';

interface KindInfo {
  label: string;
  extensions: string[];
  mime: string[];
}

export const FILE_KINDS: Record<FileKind, KindInfo> = {
  pdf: { label: 'PDF', extensions: ['.pdf'], mime: ['application/pdf'] },
  png: { label: 'PNG', extensions: ['.png'], mime: ['image/png'] },
  jpeg: { label: 'JPEG', extensions: ['.jpg', '.jpeg'], mime: ['image/jpeg'] },
  docx: { label: 'Word (.docx)', extensions: ['.docx'], mime: ['application/vnd.openxmlformats-officedocument.wordprocessingml.document'] },
  xlsx: { label: 'Excel (.xlsx)', extensions: ['.xlsx'], mime: ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'] },
};

export const SCANS: FileKind[] = ['pdf', 'png', 'jpeg'];
export const DEFAULT_MAX_BYTES = 10 * 1024 * 1024;

/** "PDF, PNG or JPEG". */
export function describeKinds(kinds: readonly FileKind[]): string {
  const names = kinds.map((k) => FILE_KINDS[k].label);
  if (names.length === 0) return 'no file type';
  if (names.length === 1) return names[0];
  return `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}`;
}

/** For `<input type="file" accept>`: extensions and MIME types of the allowed kinds. */
export const acceptAttribute = (kinds: readonly FileKind[]): string =>
  kinds.flatMap((k) => [...FILE_KINDS[k].extensions, ...FILE_KINDS[k].mime]).join(',');

/** "10 MB", "1.5 MB", "100 KB": how a size limit reads in a message. */
export function formatLimit(bytes: number): string {
  const trim = (n: number) => String(Math.round(n * 10) / 10);
  return bytes >= 1024 * 1024 ? `${trim(bytes / (1024 * 1024))} MB` : `${trim(bytes / 1024)} KB`;
}

export interface FileLike {
  name: string;
  size: number;
  type?: string;
}

const extensionOf = (name: string): string => {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
};

/** Why the file cannot be sent, in words fit to show the user, or null if it looks fine. */
export function checkFile(file: FileLike, kinds: readonly FileKind[], maxBytes: number = DEFAULT_MAX_BYTES): string | null {
  if (file.size === 0) return 'The file is empty.';
  if (file.size > maxBytes) return `The file is larger than the ${formatLimit(maxBytes)} limit.`;

  const extension = extensionOf(file.name);
  const mime = (file.type ?? '').toLowerCase();
  const allowed = kinds.some((k) => FILE_KINDS[k].extensions.includes(extension) || (mime !== '' && FILE_KINDS[k].mime.includes(mime)));
  return allowed ? null : `Only ${describeKinds(kinds)} files are accepted.`;
}

/** "1.2 MB" for a file's size next to its name. */
export const formatSize = (bytes: number): string => (bytes < 1024 ? `${bytes} B` : formatLimit(bytes));

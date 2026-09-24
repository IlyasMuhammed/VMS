import { Component, computed, input, output, signal } from '@angular/core';
import { ProgressBarModule } from 'primeng/progressbar';
import { Observable } from 'rxjs';
import { errorMessage } from '../core/api-error';
import { DEFAULT_MAX_BYTES, FileKind, SCANS, acceptAttribute, checkFile, describeKinds, formatLimit, formatSize } from '../core/file-rules';
import { UploadEvent } from '../core/upload';

/**
 * Pick or drop one file. It checks kind, size and emptiness in the browser, in the same words the server uses
 * (the server checks the file's real bytes and has the last say), then either hands the file to you
 * (`(picked)`) or, if you give it an `upload` function, sends it and shows progress (`(uploaded)` with the answer).
 *
 * ```html
 * <vms-file-upload [kinds]="['pdf','png','jpeg']" [maxBytes]="10 * 1024 * 1024"
 *                  [upload]="(f) => uploadFile(http, url, f)" (uploaded)="saved($event)" />
 * ```
 */
@Component({
  selector: 'vms-file-upload',
  standalone: true,
  imports: [ProgressBarModule],
  template: `
    <div class="drop" [class.over]="over()" [class.disabled]="disabled()" (dragover)="dragOver($event)" (dragleave)="over.set(false)" (drop)="drop($event)">
      @if (file(); as f) {
        <div class="file">
          <i class="pi pi-file" aria-hidden="true"></i>
          <span class="name">{{ f.name }}</span>
          <span class="muted">{{ size(f.size) }}</span>
          @if (state() === 'done') { <span class="ok">Uploaded</span> }
          <button type="button" class="link" [disabled]="state() === 'uploading'" (click)="remove()">Remove</button>
        </div>
        @if (state() === 'uploading') { <p-progressbar [value]="progress()" [showValue]="true" [attr.aria-label]="'Uploading ' + f.name" /> }
      } @else {
        <div class="prompt">
          <button type="button" class="choose" [disabled]="disabled()" (click)="picker.click()">{{ label() }}</button>
          <span>or drop it here.</span>
        </div>
        <div class="muted rules">{{ rules() }}</div>
      }
      <input #picker type="file" class="sr-only" tabindex="-1" [accept]="accept()" [disabled]="disabled()" (change)="picked_($event)" />
      @if (problem(); as p) { <div class="alert error" role="alert">{{ p }}</div> }
    </div>
  `,
  styles: [
    `
      .drop { display: flex; flex-direction: column; gap: .5rem; border: 2px dashed var(--vms-border-strong); border-radius: var(--vms-radius); padding: 1rem; background: var(--vms-surface-soft); }
      .drop.over { border-color: var(--vms-brand); background: var(--vms-brand-tint); }
      .drop.disabled { opacity: .6; }
      .prompt { display: flex; align-items: center; gap: .5rem; flex-wrap: wrap; }
      .choose { border: 1px solid var(--vms-brand); background: var(--vms-surface); color: var(--vms-brand-text); border-radius: var(--vms-radius-sm); padding: .4rem .8rem; font: inherit; font-weight: 600; cursor: pointer; }
      .choose:disabled { cursor: not-allowed; }
      .rules { font-size: .85rem; }
      .file { display: flex; align-items: center; gap: .6rem; flex-wrap: wrap; }
      .name { font-weight: 600; word-break: break-all; }
      .ok { color: var(--vms-success-text); font-weight: 600; }
      .link { border: 0; background: none; padding: 0; font: inherit; cursor: pointer; color: var(--vms-brand-text); text-decoration: underline; margin-left: auto; }
      .link:disabled { cursor: not-allowed; opacity: .5; }
      .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    `,
  ],
})
export class FileUploadComponent {
  readonly kinds = input<readonly FileKind[]>(SCANS);
  readonly maxBytes = input(DEFAULT_MAX_BYTES);
  /** If given, a valid file is sent with it straight away and progress is shown. */
  readonly upload = input<((file: File) => Observable<UploadEvent>) | undefined>(undefined);
  readonly label = input('Choose a file');
  readonly disabled = input(false);

  /** A file that passed the checks, whether or not an `upload` function is set. */
  readonly picked = output<File>();
  /** The server's answer once an `upload` has finished. */
  readonly uploaded = output<unknown>();
  /** The person removed the file. */
  readonly cleared = output<void>();

  protected readonly file = signal<File | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly progress = signal(0);
  protected readonly state = signal<'idle' | 'uploading' | 'done'>('idle');
  protected readonly over = signal(false);

  protected readonly accept = computed(() => acceptAttribute(this.kinds()));
  protected readonly rules = computed(() => `${describeKinds(this.kinds())}, up to ${formatLimit(this.maxBytes())}.`);
  protected readonly size = formatSize;

  protected dragOver(event: DragEvent): void {
    if (this.disabled()) return;
    event.preventDefault();
    this.over.set(true);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    if (this.disabled()) return;
    const dropped = event.dataTransfer?.files;
    if (dropped && dropped.length > 1) {
      this.problem.set('Drop one file at a time.');
      return;
    }
    if (dropped?.[0]) this.take(dropped[0]);
  }

  protected picked_(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.[0];
    input.value = ''; // so choosing the same file again after removing it still fires
    if (chosen) this.take(chosen);
  }

  private take(chosen: File): void {
    const problem = checkFile(chosen, this.kinds(), this.maxBytes());
    this.problem.set(problem);
    if (problem) return;

    this.file.set(chosen);
    this.state.set('idle');
    this.picked.emit(chosen);
    const send = this.upload();
    if (send) this.send(send, chosen);
  }

  private send(upload: (file: File) => Observable<UploadEvent>, chosen: File): void {
    this.state.set('uploading');
    this.progress.set(0);
    let result: unknown;
    upload(chosen).subscribe({
      next: (e) => {
        if (e.progress !== undefined) this.progress.set(e.progress);
        if (e.result !== undefined) result = e.result;
      },
      error: (err) => {
        this.state.set('idle');
        this.file.set(null);
        this.problem.set(errorMessage(err, 'The file could not be uploaded.'));
      },
      complete: () => {
        this.state.set('done');
        this.uploaded.emit(result);
      },
    });
  }

  protected remove(): void {
    this.file.set(null);
    this.problem.set(null);
    this.state.set('idle');
    this.cleared.emit();
  }
}

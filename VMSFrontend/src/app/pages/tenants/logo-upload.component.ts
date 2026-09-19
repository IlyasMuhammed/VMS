import { Component, effect, input, model, signal, computed } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { LogoVariant } from '../../core/models';

const ALLOWED_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
/** Keep in step with TenantLogoRules.MaxBytes on the server. */
export const MAX_LOGO_BYTES = 512 * 1024;

/**
 * One logo slot (light mode or dark mode). The preview sits on a light or dark surface, so the
 * person uploading can see how the logo will really look. It only picks and validates a file: the
 * parent decides when to send it.
 *
 *   file     the newly chosen file, or null
 *   removed  true when the existing logo (currentUrl) should be deleted on save
 */
@Component({
  selector: 'app-logo-upload',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <div class="head">
      <label>{{ label() }}</label>
      <span class="hint">{{ hint() }}</span>
    </div>

    <div
      class="preview"
      [class.preview--light]="variant() === 'light'"
      [class.preview--dark]="variant() === 'dark'"
      [class.over]="dragging()"
      (dragover)="onDragOver($event)"
      (dragleave)="dragging.set(false)"
      (drop)="onDrop($event)"
    >
      @if (previewUrl(); as src) {
        <img [src]="src" [alt]="label() + ' preview'" />
      } @else {
        <div class="empty">
          <i class="pi pi-image" aria-hidden="true"></i>
          <span>{{ removed() ? 'Will be removed when you save' : 'Drop an image here' }}</span>
        </div>
      }
    </div>

    <div class="row">
      <input #picker type="file" hidden accept="image/png,image/jpeg,image/webp" (change)="onPick($event)" />
      <p-button [label]="previewUrl() ? 'Replace' : 'Choose file'" icon="pi pi-upload" size="small" severity="secondary" (onClick)="picker.click()" />
      @if (previewUrl()) {
        <p-button label="Remove" icon="pi pi-trash" size="small" [text]="true" severity="danger" (onClick)="remove()" />
      }
      @if (removed()) {
        <p-button label="Undo" icon="pi pi-undo" size="small" [text]="true" severity="secondary" (onClick)="removed.set(false)" />
      }
      @if (file(); as f) {
        <span class="name">{{ f.name }}</span>
      }
    </div>
    @if (error()) {
      <span class="error" role="alert">{{ error() }}</span>
    }
  `,
  styles: [
    `
      :host { display: flex; flex-direction: column; min-width: 0; }
      .head { display: flex; flex-direction: column; gap: .1rem; margin-bottom: .4rem; }
      label { font-weight: 600; font-size: .85rem; }
      .hint { color: var(--vms-muted); font-size: .8rem; }

      .preview {
        height: 96px;
        display: flex;
        align-items: center;
        justify-content: center;
        overflow: hidden;
        border: 1px dashed var(--vms-border-strong);
        border-radius: var(--vms-radius-sm);
        transition: border-color .15s, box-shadow .15s;
      }
      .preview--light { background: var(--vms-neutral-0); color: var(--vms-neutral-500); }
      .preview--dark { background: var(--vms-neutral-900); color: var(--vms-neutral-400); }
      .preview.over { border-style: solid; border-color: var(--vms-brand); box-shadow: 0 0 0 3px var(--vms-brand-tint); }
      .preview img { max-width: 82%; max-height: 68px; object-fit: contain; }
      .empty { display: flex; flex-direction: column; align-items: center; gap: .25rem; font-size: .8rem; text-align: center; padding: 0 .5rem; }
      .empty i { font-size: 1.4rem; }

      .row { display: flex; align-items: center; flex-wrap: wrap; gap: .5rem; margin-top: .5rem; }
      .name { color: var(--vms-muted); font-size: .8rem; overflow-wrap: anywhere; }
      .error { color: var(--vms-danger); font-size: .8rem; margin-top: .35rem; }
    `,
  ],
})
export class LogoUploadComponent {
  readonly variant = input.required<LogoVariant>();
  readonly label = input.required<string>();
  readonly hint = input('');
  /** Object URL of the logo already stored for this tenant, when editing. */
  readonly currentUrl = input<string | null>(null);

  readonly file = model<File | null>(null);
  readonly removed = model(false);

  readonly dragging = signal(false);
  readonly error = signal('');
  private readonly chosenUrl = signal<string | null>(null);

  readonly previewUrl = computed(() => this.chosenUrl() ?? (this.removed() ? null : this.currentUrl()));

  constructor() {
    // Object URL for the chosen file, released when it changes or the component goes away.
    effect((onCleanup) => {
      const f = this.file();
      if (!f) {
        this.chosenUrl.set(null);
        return;
      }
      const url = URL.createObjectURL(f);
      this.chosenUrl.set(url);
      onCleanup(() => URL.revokeObjectURL(url));
    });
  }

  onPick(e: Event): void {
    const input = e.target as HTMLInputElement;
    this.accept(input.files?.[0]);
    input.value = ''; // so choosing the same file again still fires
  }

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.dragging.set(true);
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.dragging.set(false);
    this.accept(e.dataTransfer?.files?.[0]);
  }

  remove(): void {
    this.error.set('');
    if (this.file()) this.file.set(null);
    else if (this.currentUrl()) this.removed.set(true);
  }

  private accept(file: File | undefined): void {
    this.error.set('');
    if (!file) return;
    if (!ALLOWED_TYPES.includes(file.type)) {
      this.error.set('Use a PNG, JPEG or WebP image.');
      return;
    }
    if (file.size > MAX_LOGO_BYTES) {
      this.error.set(`That file is ${Math.ceil(file.size / 1024)} KB. The limit is ${MAX_LOGO_BYTES / 1024} KB.`);
      return;
    }
    this.removed.set(false);
    this.file.set(file);
  }
}

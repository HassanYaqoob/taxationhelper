import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorText } from '../../core/api/api.service';
import { Doc } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, takeFile } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-documents',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Documents</h1><p>Invoices, receipts, contracts and statements. LHDN expects records to be kept for 7 years.</p></div>
  @if (ws.canEdit()) {
    <div class="acts">
      <input class="input" style="max-width: 240px" placeholder="Note (optional)" aria-label="Note" [(ngModel)]="note">
      <label class="btn primary filebtn"><k-icon name="up" [size]="16" />Upload<input type="file" (change)="upload($event)" aria-label="Upload a document"></label>
    </div>
  }
</div>
<section class="card"><div class="tw"><table class="t">
  <thead><tr><th>File</th><th>Linked to</th><th>Note</th><th class="n">Size</th><th>Uploaded</th><th></th></tr></thead>
  <tbody>
    @for (d of docs(); track d.id) {
      <tr>
        <td><button type="button" class="btn ghost sm" (click)="download(d)"><k-icon name="doc" [size]="14" />{{ d.fileName }}</button></td>
        <td class="caption desc">{{ d.linkedTo || '—' }}</td>
        <td class="caption">{{ d.note }}</td>
        <td class="n caption">{{ size(d.size) }}</td>
        <td class="nowrap">{{ d.uploadedAt | day }}</td>
        <td class="right">@if (ws.canEdit()) { <button type="button" class="iconbtn" aria-label="Delete document" (click)="remove(d)"><k-icon name="trash" [size]="16" /></button> }</td>
      </tr>
    } @empty { <tr><td colspan="6"><div class="empty">No documents yet. Attach receipts to bank lines or upload them here.</div></td></tr> }
  </tbody>
</table></div></section>`
})
export class DocumentsPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly docs = signal<Doc[]>([]);
  protected note = '';

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    try { this.docs.set(await this.api.get<Doc[]>(`${this.ws.api()}/documents`)); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected size(bytes: number): string {
    return bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  protected async upload(event: Event): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    if (this.note.trim()) form.append('note', this.note.trim());
    try { await this.api.upload(`${this.ws.api()}/documents`, form); this.note = ''; this.toasts.show('Uploaded.'); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected download(d: Doc): void {
    this.api.download(`${this.ws.api()}/documents/${d.id}/file`, undefined, d.fileName).catch(e => this.toasts.error(errorText(e)));
  }

  protected async remove(d: Doc): Promise<void> {
    if (!confirm(`Delete ${d.fileName}?`)) return;
    try { await this.api.delete(`${this.ws.api()}/documents/${d.id}`); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}

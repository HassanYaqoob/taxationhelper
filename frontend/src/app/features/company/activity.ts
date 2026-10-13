import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiService, errorText } from '../../core/api/api.service';
import { AuditRow } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
<div class="page-head"><div><h1>Activity log</h1><p>Who changed what, and when. The log can't be edited.</p></div></div>
<section class="card"><div class="tw"><table class="t">
  <thead><tr><th>When</th><th>Who</th><th>Action</th><th>Detail</th></tr></thead>
  <tbody>
    @for (a of rows(); track $index) {
      <tr><td class="nowrap caption">{{ when(a.at) }}</td><td>{{ a.user }}</td><td><span class="pill gr">{{ a.action }}</span></td><td class="desc">{{ a.detail }}</td></tr>
    } @empty { <tr><td colspan="4"><div class="empty">Nothing yet.</div></td></tr> }
  </tbody>
</table></div></section>`
})
export class ActivityPage {
  private readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly rows = signal<AuditRow[]>([]);
  private readonly fmt = new Intl.DateTimeFormat('en-MY', { dateStyle: 'medium', timeStyle: 'short' });

  constructor() {
    this.api.get<AuditRow[]>(`${this.ws.api()}/activity`).then(r => this.rows.set(r)).catch(e => this.toasts.error(errorText(e)));
  }

  protected when(at: string): string { return this.fmt.format(new Date(at.endsWith('Z') ? at : at + 'Z')); }
}

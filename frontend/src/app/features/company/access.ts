import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorText } from '../../core/api/api.service';
import { Member } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, initials } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-access',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, DayPipe],
  template: `
<div class="page-head"><div><h1>Accountant access</h1><p>Give your tax agent or accountant read-only access to this company's books, reports and documents.</p></div></div>
<div class="grid g21">
  <section class="card">
    @for (m of members(); track m.userId) {
      <div class="list-row">
        <span class="av">{{ initials(m.name) }}</span>
        <div class="grow"><div>{{ m.name }} <span class="pill" [class.ok]="m.role === 'Owner'" [class.in]="m.role !== 'Owner'">{{ m.role === 'Owner' ? 'Owner' : 'Read-only' }}</span></div>
          <div class="caption">{{ m.email }} · since {{ m.since | day }}</div></div>
        @if (m.role !== 'Owner') { <button type="button" class="btn sm danger" (click)="remove(m)">Remove</button> }
      </div>
    }
  </section>
  <form class="card pad stack" (ngSubmit)="add()">
    <h2>Invite an accountant</h2>
    <p class="caption">If they already have a Kira account, enter their email. Otherwise set a temporary password and share it with them privately.</p>
    <label class="field">Email<input class="input" type="email" name="email" required [(ngModel)]="email"></label>
    <label class="field">Name (new accounts)<input class="input" name="name" [(ngModel)]="name"></label>
    <label class="field">Temporary password (new accounts)<input class="input" type="password" name="password" autocomplete="new-password" [(ngModel)]="password"></label>
    <button type="submit" class="btn primary"><k-icon name="shield" [size]="16" />Give access</button>
  </form>
</div>`
})
export class AccessPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly members = signal<Member[]>([]);
  protected readonly initials = initials;
  protected email = '';
  protected name = '';
  protected password = '';

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    try { this.members.set(await this.api.get<Member[]>(`${this.ws.api()}/members`)); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async add(): Promise<void> {
    try {
      const r = await this.api.post<{ created: boolean }>(`${this.ws.api()}/members`, { email: this.email, name: this.name || null, password: this.password || null });
      this.toasts.show(r.created ? 'Account created and access given. Share the password privately.' : 'Access given.');
      this.email = this.name = this.password = '';
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async remove(m: Member): Promise<void> {
    if (!confirm(`Remove ${m.name}'s access?`)) return;
    try { await this.api.delete(`${this.ws.api()}/members/${m.userId}`); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}

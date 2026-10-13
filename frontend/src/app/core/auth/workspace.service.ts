import { Injectable, computed, signal } from '@angular/core';
import { Workspace } from '../api/models';

/** The workspace the shell is showing, set from the URL. Pages read the company id and edit rights from here. */
@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  readonly current = signal<Workspace | null>(null);
  readonly isCompany = computed(() => this.current()?.kind === 'company');
  readonly canEdit = computed(() => this.current()?.role === 'Owner');
  readonly companyId = computed(() => this.isCompany() ? this.current()!.id : '');
  /** API path prefix for the current company, e.g. /companies/{id}. */
  readonly api = computed(() => `/companies/${this.companyId()}`);
  /** Router prefix for the current company pages. */
  readonly base = computed(() => `/c/${this.companyId()}`);
}

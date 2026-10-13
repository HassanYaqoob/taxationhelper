import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CompanySetup, MONTHS } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';

/** Company details and bank accounts, used at sign-up and when adding another company. Edits the given object in place. */
@Component({
  selector: 'k-company-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon],
  template: `
<div class="form-grid">
  <label class="field full">Registered company name
    <input class="input" name="companyName" [(ngModel)]="v.name" placeholder="e.g. ABC Software Sdn. Bhd." required></label>
  <label class="field">SSM registration number
    <input class="input" name="regNo" [(ngModel)]="v.registrationNo" placeholder="e.g. 202301012345 (1501234-X)"></label>
  <label class="field">Incorporation date
    <input class="input" type="date" name="incorporatedOn" [(ngModel)]="v.incorporatedOn"><span class="hint">Used for SSM annual return reminders.</span></label>
  <label class="field">Financial year starts in
    <select class="input" name="fyStartMonth" [(ngModel)]="v.fyStartMonth">
      @for (m of months; track $index) { <option [ngValue]="$index + 1">{{ m }}</option> }
    </select></label>
  <label class="field">What the business does
    <input class="input" name="industry" [(ngModel)]="v.industry" placeholder="e.g. software development, café, trading"><span class="hint">Helps the AI sort bank lines.</span></label>
  <div class="full">
    <div class="field" style="margin-bottom: 6px">Bank accounts</div>
    @for (b of v.banks; track $index; let i = $index) {
      <div class="row" style="margin-bottom: 8px">
        <input class="input" [name]="'bankName' + i" [(ngModel)]="b.name" placeholder="e.g. Maybank Business Current" aria-label="Bank account name">
        <input class="input" style="max-width: 130px" [name]="'bankLast4' + i" [(ngModel)]="b.last4" placeholder="Last 4 digits" aria-label="Last 4 digits" inputmode="numeric" maxlength="4">
        @if (v.banks.length > 1) { <button type="button" class="iconbtn" aria-label="Remove bank account" (click)="v.banks.splice(i, 1)"><k-icon name="x" [size]="16" /></button> }
      </div>
    }
    <button type="button" class="btn sm" (click)="v.banks.push({ name: '', last4: '' })"><k-icon name="plus" [size]="14" />Add another bank account</button>
  </div>
</div>`
})
export class CompanyForm {
  readonly value = input.required<CompanySetup>();
  /** The setup object being edited (fields are bound with ngModel and changed in place). */
  protected get v(): CompanySetup { return this.value(); }
  protected readonly months = MONTHS;
}

export function emptyCompany(): CompanySetup {
  return { name: '', registrationNo: '', incorporatedOn: null, fyStartMonth: 1, industry: '', banks: [{ name: '', last4: '' }] };
}

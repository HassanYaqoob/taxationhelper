import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHost } from './core/ui/toast.service';

@Component({
  selector: 'k-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, ToastHost],
  template: `<router-outlet /><k-toasts />`
})
export class App {}

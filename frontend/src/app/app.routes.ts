import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/guards';
import { Shell } from './layout/shell';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login').then(m => m.LoginPage), title: 'Sign in · Kira' },
  { path: 'register', canActivate: [guestGuard], loadComponent: () => import('./features/auth/register').then(m => m.RegisterPage), title: 'Create account · Kira' },
  { path: 'welcome', canActivate: [authGuard], loadComponent: () => import('./features/auth/welcome').then(m => m.WelcomePage), title: 'Add a workspace · Kira' },
  {
    path: 'c/:cid', component: Shell, canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./features/company/dashboard').then(m => m.DashboardPage), title: 'Dashboard · Kira' },
      { path: 'banking', loadComponent: () => import('./features/company/banking').then(m => m.BankingPage), title: 'Bank statements · Kira' },
      { path: 'transactions', loadComponent: () => import('./features/company/transactions').then(m => m.TransactionsPage), title: 'Transactions · Kira' },
      { path: 'reports', loadComponent: () => import('./features/company/reports').then(m => m.ReportsPage), title: 'Reports · Kira' },
      { path: 'tax', loadComponent: () => import('./features/company/tax').then(m => m.TaxPage), title: 'Tax & compliance · Kira' },
      { path: 'documents', loadComponent: () => import('./features/company/documents').then(m => m.DocumentsPage), title: 'Documents · Kira' },
      { path: 'access', loadComponent: () => import('./features/company/access').then(m => m.AccessPage), title: 'Accountant access · Kira' },
      { path: 'settings', loadComponent: () => import('./features/company/settings').then(m => m.SettingsPage), title: 'Settings · Kira' },
      { path: 'activity', loadComponent: () => import('./features/company/activity').then(m => m.ActivityPage), title: 'Activity log · Kira' },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  {
    path: 'p', component: Shell, canActivate: [authGuard],
    children: [
      { path: 'overview', loadComponent: () => import('./features/personal/overview').then(m => m.OverviewPage), title: 'Overview · Kira' },
      { path: 'accounts', loadComponent: () => import('./features/personal/accounts').then(m => m.AccountsPage), title: 'Accounts · Kira' },
      { path: 'receipts', loadComponent: () => import('./features/personal/receipts').then(m => m.ReceiptsPage), title: 'Receipts · Kira' },
      { path: 'receipts/:id', loadComponent: () => import('./features/personal/receipt-detail').then(m => m.ReceiptDetailPage), title: 'Receipt · Kira' },
      { path: 'transactions', loadComponent: () => import('./features/personal/transactions').then(m => m.PersonalTransactionsPage), title: 'Transactions · Kira' },
      { path: 'reliefs', loadComponent: () => import('./features/personal/reliefs').then(m => m.ReliefsPage), title: 'Tax reliefs · Kira' },
      { path: 'reports', loadComponent: () => import('./features/personal/reports').then(m => m.PersonalReportsPage), title: 'Reports · Kira' },
      { path: '', pathMatch: 'full', redirectTo: 'overview' }
    ]
  },
  { path: '', pathMatch: 'full', canActivate: [authGuard], loadComponent: () => import('./features/auth/welcome').then(m => m.HomeRedirect) },
  { path: '**', redirectTo: '' }
];

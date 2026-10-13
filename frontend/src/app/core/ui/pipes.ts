import { Pipe, PipeTransform } from '@angular/core';
import { MONTHS } from '../api/models';

const money2 = new Intl.NumberFormat('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const money0 = new Intl.NumberFormat('en-MY', { maximumFractionDigits: 0 });

/** RM 1,234.56. 'sign' adds + to positive amounts; '0' rounds to whole ringgit. */
@Pipe({ name: 'rm' })
export class RmPipe implements PipeTransform {
  transform(value: number | null | undefined, mode: '' | 'sign' | '0' = ''): string {
    const n = value ?? 0;
    const body = mode === '0' ? money0.format(Math.abs(n)) : money2.format(Math.abs(n));
    const sign = n < 0 ? '−' : mode === 'sign' && n > 0 ? '+' : '';
    return `${sign}RM ${body}`;
  }
}

/** 2026-09-30 → 30 Sep 2026. */
@Pipe({ name: 'day' })
export class DayPipe implements PipeTransform {
  transform(value: string | null | undefined): string { return formatDay(value); }
}

export function formatDay(value: string | null | undefined): string {
  if (!value) return '—';
  const [y, m, d] = value.slice(0, 10).split('-').map(Number);
  if (!y || !m || !d) return value;
  return `${d} ${MONTHS[m - 1]} ${y}`;
}

/** 2026-09 → Sep 2026. */
export function monthName(ym: string): string {
  return `${MONTHS[Number(ym.slice(5, 7)) - 1]} ${ym.slice(0, 4)}`;
}

export function initials(s: string): string {
  return s.split(/\s+/).filter(w => /^[A-Za-z]/.test(w)).slice(0, 2).map(w => w[0]).join('').toUpperCase();
}

export function percent(value: number, max: number): number {
  return max > 0 ? Math.max(0, Math.min(100, Math.round(value / max * 100))) : 0;
}

/** First file chosen in a file input, and clears the input so the same file can be chosen again. */
export function takeFile(event: Event): File | null {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0] ?? null;
  input.value = '';
  return file;
}

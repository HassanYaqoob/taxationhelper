// Mirrors the API's DTOs (JSON is camelCase; enums are strings).

export type Role = 'Owner' | 'Accountant';
export type Direction = 'In' | 'Out';
export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';
export type TaxTreatment = 'Standard' | 'Bank' | 'Capital' | 'HalfDeductible' | 'NonDeductible';
export type BankLineStatus = 'Pending' | 'Posted' | 'Ignored';
export type PaymentKind = 'Bank' | 'CreditCard' | 'DebitCard' | 'EWallet' | 'Cash';
export type PaymentStatus = 'Unknown' | 'Cash' | 'Account' | 'NotPaid';
export type PaymentSource = 'None' | 'Manual' | 'Receipt' | 'Screenshot';
export type ReceiptStatus = 'Review' | 'Saved' | 'Error';
export type TransactionSource = 'Manual' | 'Statement' | 'Receipt';
export type ReminderRepeat = 'Once' | 'Monthly' | 'Yearly';

// ---------- Auth ----------
export interface Workspace { kind: 'personal' | 'company'; id: string; name: string; role: Role; }
export interface Me { id: string; name: string; email: string; title: string; usesPersonal: boolean; workspaces: Workspace[]; aiEnabled: boolean; }
export interface AuthResponse { accessToken: string; expiresAt: string; me: Me; }
export interface BankSetup { name: string; last4: string; }
export interface CompanySetup { name: string; registrationNo: string; incorporatedOn: string | null; fyStartMonth: number; industry: string; banks: BankSetup[]; }

// ---------- Company ----------
export interface Company {
  id: string; name: string; registrationNo: string; incorporatedOn: string | null; industry: string; address: string; fyStartMonth: number;
  isSme: boolean; cp204Estimate: number; employeeCount: number; role: Role;
}
export interface Account { id: string; code: string; name: string; type: AccountType; tax: TaxTreatment; systemKey: string | null; hint: string; last4: string; balance: number; }
export interface BankSummary { id: string; code: string; name: string; last4: string; balance: number; pendingCount: number; lastLineDate: string | null; }
export interface BankingOverview { banks: BankSummary[]; pending: number; pendingUnsure: number; pendingCash: number; pendingForeign: number; aiEnabled: boolean; }
export interface BankLine {
  id: string; bankAccountId: string; bankName: string; date: string; description: string; direction: Direction; amount: number;
  accountId: string | null; accountName: string | null; confidence: number; reason: string; flags: string[]; status: BankLineStatus;
  journalEntryId: string | null; documents: number; sourceFile: string;
}
export interface ImportResult { added: number; duplicates: number; usedAi: boolean; message: string; }
export interface SetAccountResult { similar: number; pattern: string; }
export interface CountResult { count: number; }
export interface EntryNumber { id: string; number: number; }
export interface RecheckSummary { rechecked: number; improved: number; }
export interface Rule { id: string; pattern: string; value: string; label: string; }
export interface Doc { id: string; fileName: string; contentType: string; size: number; note: string; bankLineId: string | null; journalEntryId: string | null; linkedTo: string | null; uploadedAt: string; }
export interface EntrySummary { id: string; number: number; date: string; memo: string; source: string; amount: number; accounts: string; reversed: boolean; isReversal: boolean; documents: number; }
export interface EntryLine { accountId: string; code: string; name: string; debit: number; credit: number; }
export interface EntryDetail {
  id: string; number: number; date: string; memo: string; source: string; createdBy: string; createdAt: string; lines: EntryLine[];
  reversedByNumber: number | null; reversesNumber: number | null; documents: Doc[];
}
export interface MonthAmount { month: string; income: number; expenses: number; }
export interface Deadline { name: string; due: string; daysLeft: number; kind: string; reminderId: string | null; }
export interface EInvoice { revenue12m: number; threshold: number; required: boolean; note: string; }
export interface Attention { kind: string; title: string; detail: string; link: string; }
export interface Dashboard {
  companyName: string; fyYear: number; fyStart: string; fyEnd: string; revenue: number; expenses: number; netProfit: number; cash: number;
  receivables: number; months: MonthAmount[]; banks: BankSummary[]; pendingLines: number; unsureLines: number; attention: Attention[];
  deadlines: Deadline[]; eInvoice: EInvoice;
}
export interface AccountAmount { id: string; code: string; name: string; type: AccountType; tax: TaxTreatment; amount: number; }
export interface Pnl { from: string; to: string; income: AccountAmount[]; expenses: AccountAmount[]; totalIncome: number; totalExpenses: number; netProfit: number; }
export interface BsRow { name: string; code: string | null; amount: number; }
export interface BalanceSheet { asAt: string; assets: BsRow[]; liabilities: BsRow[]; equity: BsRow[]; totalAssets: number; totalLiabilities: number; totalEquity: number; balanced: boolean; }
export interface TrialBalance { asAt: string; rows: { code: string; name: string; debit: number; credit: number }[]; totalDebit: number; totalCredit: number; }
export interface GeneralLedger {
  accountId: string; code: string; name: string; from: string; to: string; opening: number; closing: number;
  rows: { entryId: string; number: number; date: string; memo: string; debit: number; credit: number; balance: number }[];
}
export interface TaxEstimate {
  yearOfAssessment: number; income: number; deductible: number; entertainment: number; entertainmentAddBack: number; nonDeductible: number;
  assetsBought: number; capitalAllowancePct: number; capitalAllowance: number; adjusted: number; chargeable: number;
  tiers: { label: string; amount: number }[]; tax: number; cp204Paid: number; balance: number; sme: boolean;
}
export interface Compliance {
  tax: TaxEstimate; from: string; to: string;
  cp204: { estimate: number; projectedTax: number; paidSoFar: number; nextRevisionMonth: number | null; nextRevisionBy: string | null; status: string; advice: string };
  audit: { year: number; criteria: { name: string; value: string; limit: string; within: boolean }[]; met: number; likelyQualifies: boolean; note: string };
  eInvoice: EInvoice; deadlines: Deadline[]; dueFromDirector: number;
  foreignPayees: { id: string; date: string; description: string; amount: number; status: string }[];
  cash: { pendingWithdrawals: number; pendingAmount: number; pettyCashBalance: number };
}
export interface Member { userId: string; name: string; email: string; title: string; role: Role; since: string; }
export interface AuditRow { at: string; user: string; action: string; detail: string; }
export interface SearchHit { kind: string; title: string; detail: string; link: string; id: string | null; }
export interface AskResponse { answer: string; }

// ---------- Personal ----------
export interface PaymentAccount { id: string; name: string; kind: PaymentKind; institution: string; last4: string; openingBalance: number; balance: number; sortOrder: number; }
export interface PTx {
  id: string; accountId: string | null; accountName: string | null; date: string; description: string; direction: Direction; amount: number; category: string;
  confidence: number; reason: string; reviewed: boolean; isTransfer: boolean; source: TransactionSource; receiptId: string | null; statementMatched: boolean;
}
export interface PImportResult { added: number; duplicates: number; matchedReceipts: number; transfers: number; usedAi: boolean; message: string; }
export interface ScreenshotView { institution: string; last4: string; amount: number; when: string | null; recipient: string; reference: string; status: string; amountMatches: boolean; why: string; }
export interface Receipt {
  id: string; status: ReceiptStatus; merchant: string; date: string; total: number; sst: number; receiptNo: string; items: { description: string; amount: number }[];
  category: string; confidence: number; reason: string; printedPayment: string; paymentStatus: PaymentStatus; paymentAccountId: string | null;
  paymentAccountName: string | null; paymentSource: PaymentSource; paymentNote: string; paymentConfidence: number; reliefCode: string | null;
  suggestedReliefCode: string | null; reliefConfidence: number; reliefReason: string; hasImage: boolean; hasScreenshot: boolean;
  screenshot: ScreenshotView | null; transactionId: string | null; statementMatched: boolean;
}
export interface ReliefLine { code: string; name: string; hint: string; limit: number; claimed: number; receipts: number; new: boolean; }
export interface ReliefSummary {
  year: number; lines: ReliefLine[]; filingNote: string;
  suggestions: { receiptId: string; merchant: string; date: string; amount: number; code: string; name: string; reason: string }[];
}
export interface PersonalOverview {
  month: string; moneyIn: number; spending: number; byCategory: { category: string; amount: number }[]; accounts: PaymentAccount[];
  recentReceipts: Receipt[]; reliefs: ReliefLine[]; attention: Attention[]; filingNote: string; months: string[];
}
export interface SpendingReport { year: number; categories: string[]; months: string[]; values: number[][]; }

export const PERSONAL_CATEGORIES = ['Income', 'Groceries', 'Dining', 'Transport', 'Fuel', 'Utilities', 'Housing', 'Entertainment', 'Shopping',
  'Health', 'Education', 'Insurance', 'Family', 'Transfers', 'Other'] as const;
export const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;
export const KIND_LABEL: Record<PaymentKind, string> = { Bank: 'Bank account', CreditCard: 'Credit card', DebitCard: 'Debit card', EWallet: 'E-wallet', Cash: 'Cash' };

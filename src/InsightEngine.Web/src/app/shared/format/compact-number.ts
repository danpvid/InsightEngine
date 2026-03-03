export interface CompactNumberOptions {
  locale?: string;
  maximumFractionDigits?: number;
}

const THOUSAND = 1_000;
const MILLION = 1_000_000;
const BILLION = 1_000_000_000;
const TRILLION = 1_000_000_000_000;

export function formatCompactNumber(value: number | null | undefined, options?: CompactNumberOptions): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }

  const locale = options?.locale ?? 'pt-BR';
  const decimals = options?.maximumFractionDigits ?? 1;
  const abs = Math.abs(value);

  if (abs < THOUSAND) {
    return new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(value);
  }

  if (abs < MILLION) {
    return `${formatScaled(value / THOUSAND, locale, decimals)}k`;
  }

  if (abs < BILLION) {
    return `${formatScaled(value / MILLION, locale, decimals)}M`;
  }

  if (abs < TRILLION) {
    return `${formatScaled(value / BILLION, locale, decimals)}B`;
  }

  return `${formatScaled(value / TRILLION, locale, decimals)}T`;
}

function formatScaled(value: number, locale: string, decimals: number): string {
  return new Intl.NumberFormat(locale, {
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals
  }).format(value);
}

import { formatCompactNumber } from './compact-number';

describe('formatCompactNumber', () => {
  it('formats values under 1000 without suffix', () => {
    expect(formatCompactNumber(950, { locale: 'pt-BR' })).toBe('950');
  });

  it('formats thousands with k suffix', () => {
    expect(formatCompactNumber(1500, { locale: 'pt-BR' })).toBe('1,5k');
  });

  it('formats millions with M suffix', () => {
    expect(formatCompactNumber(1500000, { locale: 'pt-BR' })).toBe('1,5M');
  });

  it('formats billions with B suffix', () => {
    expect(formatCompactNumber(1500000000, { locale: 'pt-BR' })).toBe('1,5B');
  });
});

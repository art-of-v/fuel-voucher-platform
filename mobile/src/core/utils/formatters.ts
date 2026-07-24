export function normalizeFuelName(name: string): string {
  const normalized = name.toLowerCase().trim();
  const fuelNameMap: Record<string, string> = {
    'дп євро': 'diesel',
    'дп': 'diesel',
    'diesel': 'diesel',
    'dp': 'diesel',
    'дп euro': 'diesel',
    'a-95': 'a-95',
    'а-95': 'a-95',
    '95': 'a-95',
    'a-95 євро': 'a-95',
    'а-95 євро': 'a-95',
    'mustang 95': 'a-95 mustang',
    'a-95 mustang': 'a-95 mustang',
    'mustang diesel': 'diesel mustang',
    'diesel mustang': 'diesel mustang',
    'dp mustang': 'diesel mustang',
    'pulls 95': 'a-95 pulls',
    'a-95 pulls': 'a-95 pulls',
    'pills 95': 'a-95 pulls',
    'a 95 euro': 'a-95 euro',
    'а 95 євро': 'a-95 euro',
    'upg-100': 'upg-100',
    '100': 'upg-100',
    'gas': 'gas',
    'lpg': 'gas',
    'газ': 'gas',
  };

  return fuelNameMap[normalized] || normalized;
}

export function formatDate(dateString: string): string {
  return new Date(dateString).toLocaleDateString();
}

export function formatExpirationDate(dateStr: string): string {
  const d = new Date(dateStr);
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = d.getFullYear();
  return `${day}.${month}.${year}`;
}

export function formatCurrency(amount: number): string {
  return `${amount.toFixed(2)} ₴`;
}

export function truncateId(id: string, length: number = 12): string {
  return id.slice(0, length).toUpperCase();
}

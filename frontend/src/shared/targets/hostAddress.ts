export function hostAddressKey(host: string): string {
  const value = host.trim();
  if (value.includes(':')) {
    const raw = value.startsWith('[') && value.endsWith(']') ? value.slice(1, -1) : value;
    if (!/^[\da-f:.]+$/i.test(raw)) return value.toUpperCase();
    try {
      const normalized = new URL(`https://[${raw}]/`).hostname.slice(1, -1);
      const mapped = /^::ffff:([\da-f]+):([\da-f]+)$/i.exec(normalized);
      if (mapped) {
        const high = parseInt(mapped[1], 16);
        const low = parseInt(mapped[2], 16);
        return `::FFFF:${high >> 8}.${high & 255}.${low >> 8}.${low & 255}`;
      }
      return normalized.toUpperCase();
    } catch {
      return value.toUpperCase();
    }
  }
  return value.replace(/\.+$/, '').toUpperCase();
}

export function isExactLocalName(host: string, machineName: string | null): boolean {
  return machineName != null && machineName.trim() !== ''
    && host.trim().toUpperCase() === machineName.trim().toUpperCase();
}

export function checkedCents(value: number): number {
    if (!Number.isSafeInteger(value) || value < 0) {
        throw new Error("Amounts must be nonnegative safe integers in cents.");
    }
    return value;
}

export function subtotal(lines: readonly OrderLine[]): number {
    return lines.reduce((total: number, line: OrderLine) => {
        if (!Number.isSafeInteger(line.quantity) || line.quantity < 1 || line.quantity > 100) {
            throw new Error(`Invalid quantity for ${line.sku}.`);
        }
        return checkedCents(total + checkedCents(line.unitPriceCents) * line.quantity);
    }, 0);
}

import { calculateSubtotal } from './subtotal.js';

/**
 * Cart pricing inputs.
 * @typedef {object} QuoteInput
 * @property {import('./subtotal.js').CartLine[]} lines
 * @property {number} discountPercent
 */

/**
 * Apply a percentage discount using integer cents.
 * @param {QuoteInput} input
 */
export function createQuote(input) {
    const subtotalCents = calculateSubtotal(input.lines);
    const discountCents = Math.round(subtotalCents * input.discountPercent / 100);
    return { subtotalCents, discountCents, totalCents: subtotalCents - discountCents };
}

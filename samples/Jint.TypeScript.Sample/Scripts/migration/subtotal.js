/**
 * One cart line, with prices expressed in cents.
 * @typedef {object} CartLine
 * @property {string} sku
 * @property {number} quantity
 * @property {number} unitPriceCents
 */

/**
 * Add the line totals. Try lines[0]?. to see JSDoc IntelliSense.
 * @param {CartLine[]} lines
 * @returns {number}
 */
export function calculateSubtotal(lines) {
    return lines.reduce((total, line) => total + line.quantity * line.unitPriceCents, 0);
}

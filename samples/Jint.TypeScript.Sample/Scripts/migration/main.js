import { createQuote } from './quote.js';

/**
 * Quote a cart using the existing JavaScript implementation.
 * Hover over input or type input. to explore its JSDoc types.
 * Convert the files one at a time; imports are updated for you.
 * @param {import('./quote.js').QuoteInput} input
 */
export function run(input) {
    const quote = createQuote(input);
    host.Log(`Quoted ${input.lines.length} cart lines`);
    return quote;
}

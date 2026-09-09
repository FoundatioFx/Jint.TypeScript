import { quoteOrder } from "./pricing/quote.ts";
import type { Quote } from "./pricing/contracts.ts";

// This exported function is the entry point invoked by the playground.
export function run(input: OrderInput): Quote {
    return quoteOrder(input);
}

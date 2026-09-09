import { subtotal, checkedCents } from "./money.ts";
import { bestDiscount } from "./discounts.ts";
import type { Quote } from "./contracts.ts";

function shippingCents(destination: string, discountedSubtotal: number): number {
    switch (destination) {
        case "US": return discountedSubtotal >= 10_000 ? 0 : 595;
        case "CA": return 1_295;
        case "GB": return 1_995;
        default: throw new Error(`Unsupported destination: ${destination}`);
    }
}

export function quoteOrder(order: OrderInput): Quote {
    if (order.lines.length === 0) throw new Error("Cannot quote an empty order.");
    const subtotalCents = subtotal(order.lines);
    const discount = bestDiscount(order, subtotalCents);
    const discountedSubtotal = checkedCents(subtotalCents - discount.amountCents);
    const shipping = shippingCents(order.destination, discountedSubtotal);
    host.Log(`Quoted order ${order.id} using ${discount.name}.`);
    return {
        orderId: order.id,
        subtotalCents,
        discount,
        shippingCents: shipping,
        totalCents: checkedCents(discountedSubtotal + shipping)
    } satisfies Quote;
}

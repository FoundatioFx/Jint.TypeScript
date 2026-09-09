import type { Discount } from "./contracts.ts";

abstract class DiscountRule {
    abstract readonly name: string;
    abstract rate(order: OrderInput): number;

    calculate(order: OrderInput, subtotalCents: number): Discount {
        return { name: this.name, amountCents: Math.round(subtotalCents * this.rate(order)) };
    }
}

class LoyaltyDiscount extends DiscountRule {
    readonly name = "Gold customer";

    override rate(order: OrderInput): number {
        return order.customer.tier === "gold" ? 0.10 : 0;
    }
}

class CouponDiscount extends DiscountRule {
    readonly name = "Coupon";
    private readonly rates: Readonly<Record<string, number>> = { SAVE15: 0.15, WELCOME5: 0.05 };

    override rate(order: OrderInput): number {
        return this.rates[order.coupon?.trim().toUpperCase() ?? ""] ?? 0;
    }
}

const rules: readonly DiscountRule[] = [new LoyaltyDiscount(), new CouponDiscount()];

// Promotions do not stack: pick the largest discount, with a stable tie break.
export function bestDiscount(order: OrderInput, subtotalCents: number): Discount {
    return rules.reduce((best: Discount, rule: DiscountRule) => {
        const candidate = rule.calculate(order, subtotalCents);
        return candidate.amountCents > best.amountCents ? candidate : best;
    }, { name: "None", amountCents: 0 });
}

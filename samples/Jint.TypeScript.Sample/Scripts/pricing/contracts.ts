// Imported with "import type": this file is not loaded or executed by the host.
export interface Discount {
    name: string;
    amountCents: number;
}

export interface Quote {
    orderId: string;
    subtotalCents: number;
    discount: Discount;
    shippingCents: number;
    totalCents: number;
}

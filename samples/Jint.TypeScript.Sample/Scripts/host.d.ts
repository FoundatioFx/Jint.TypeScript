// Editor/type-checker declarations only. The C# host never loads this file.
// Property/method casing matches HostServices.cs; JSON input uses camelCase.
interface HostServices {
    readonly RequestId: string;
    readonly ReceivedAt: string;
    Log(message: string): void;
}

interface OrderLine {
    sku: string;
    quantity: number;
    unitPriceCents: number;
}

interface OrderInput {
    id: string;
    customer: { email: string; tier: "standard" | "gold" };
    destination: string;
    coupon?: string;
    lines: OrderLine[];
}

declare const host: HostServices;

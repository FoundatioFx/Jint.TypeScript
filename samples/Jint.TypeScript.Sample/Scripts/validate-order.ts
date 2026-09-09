interface ValidationIssue {
    field: string;
    code: "required" | "invalid" | "out-of-range";
    message: string;
}

interface ValidationResult {
    orderId: string;
    valid: boolean;
    issues: ValidationIssue[];
}

// The host supplies the OrderInput shape. These are business validation rules,
// such as those a tenant might customize before accepting an order.
export function run(input: OrderInput): ValidationResult {
    const issues: ValidationIssue[] = [];
    if (!input.id.trim()) {
        issues.push({ field: "id", code: "required", message: "An order ID is required." });
    }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(input.customer.email)) {
        issues.push({ field: "customer.email", code: "invalid", message: "Enter a valid email address." });
    }
    if (input.lines.length === 0) {
        issues.push({ field: "lines", code: "required", message: "Add at least one order line." });
    }

    input.lines.forEach((line: OrderLine, index: number) => {
        if (!Number.isSafeInteger(line.quantity) || line.quantity < 1 || line.quantity > 100) {
            issues.push({ field: `lines[${index}].quantity`, code: "out-of-range", message: "Quantity must be between 1 and 100." });
        }
        if (!Number.isSafeInteger(line.unitPriceCents) || line.unitPriceCents < 0) {
            issues.push({ field: `lines[${index}].unitPriceCents`, code: "invalid", message: "Price must be a nonnegative integer in cents." });
        }
    });

    host.Log(`Validated order ${input.id}: ${issues.length} issue(s).`);
    return { orderId: input.id, valid: issues.length === 0, issues } satisfies ValidationResult;
}

// Enums aren't supported. Use an as const object for named values
// and derive a union type for annotations: "normal" | "high".
const Severity = {
    Normal: "normal",
    High: "high"
} as const;
type Severity = typeof Severity[keyof typeof Severity];

interface NormalizedTicket {
    externalId: string;
    title: string;
    severity: Severity;
    tags: Record<string, string>;
    receivedAt: string;
    requestId: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}

// External payloads are unknown until runtime checks establish their shape.
export function run(payload: unknown): NormalizedTicket {
    if (!isRecord(payload) || payload.type !== "ticket.created" || typeof payload.id !== "string") {
        throw new Error("Expected a ticket.created webhook with a string ID.");
    }
    const data = payload.data;
    if (!isRecord(data) || typeof data.subject !== "string" || typeof data.priority !== "string") {
        throw new Error("The webhook must contain a subject and priority.");
    }

    // Copy only agreed public fields; tokens, customer data and arbitrary
    // upstream metadata are not copied to the downstream event.
    const tags: Record<string, string> = {};
    if (isRecord(payload.metadata)) {
        for (const key of ["environment", "service", "region"] as const) {
            const value = payload.metadata[key];
            if (typeof value === "string") tags[key] = value;
        }
    }

    host.Log(`Normalized webhook ${payload.id}.`);
    return {
        externalId: payload.id,
        title: data.subject.trim(),
        severity: data.priority === "urgent" || data.priority === "high" ? Severity.High : Severity.Normal,
        tags,
        receivedAt: host.ReceivedAt,
        requestId: host.RequestId
    } satisfies NormalizedTicket;
}

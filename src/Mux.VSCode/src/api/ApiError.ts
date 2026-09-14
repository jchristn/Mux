/**
 * The single error type every failed server call throws. Carries the HTTP status and the raw response body
 * so callers can branch on the status (a 401 is an auth problem, a 404 an unknown endpoint or session) and
 * still surface the server's own message. Distinct from a generic {@link Error} so `instanceof ApiError`
 * cleanly separates transport failures from programming errors.
 */
export class ApiError extends Error {
    /** The HTTP status code of the failed response. */
    public readonly status: number;

    /** The raw response body, when one was returned. */
    public readonly body: string;

    /**
     * Creates an API error.
     *
     * @param status The HTTP status code.
     * @param body The raw response body (may be empty).
     * @param message An optional message; a readable default is derived from the body or status when omitted.
     */
    public constructor(status: number, body: string, message?: string) {
        super(message ?? ApiError.deriveMessage(status, body));
        this.name = 'ApiError';
        this.status = status;
        this.body = body;
    }

    private static deriveMessage(status: number, body: string): string {
        if (body) {
            try {
                const parsed = JSON.parse(body) as { message?: string; Message?: string };
                const detail = parsed.message ?? parsed.Message;
                if (detail) {
                    return detail;
                }
            } catch {
                // Body was not JSON; fall through to the status-based message.
            }
        }

        return `Request failed with status ${status}`;
    }
}

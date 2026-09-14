/**
 * The API contract the extension is built against, and the check that decides whether a discovered server is
 * safe to talk to. Refusing an incompatible server up front turns a confusing mid-run failure into a clear
 * "upgrade mux" message. Pure and unit-testable.
 */

/** The major contract version this build of the extension supports. */
export const SUPPORTED_CONTRACT_MAJOR = 1;

/** The result of a contract compatibility check. */
export interface ContractCheck {
    /** Whether the server's contract is compatible with this extension. */
    compatible: boolean;

    /** A human-readable reason when incompatible; empty when compatible. */
    reason: string;
}

/**
 * Checks a server's reported contract version against {@link SUPPORTED_CONTRACT_MAJOR}. A matching major is
 * compatible; a lower major means the server is too old, a higher one means the extension is too old, and an
 * unparseable value is treated as incompatible.
 *
 * @param serverContractVersion The `contractVersion` from `GET /v1.0/api/health`.
 * @returns The compatibility result.
 */
export function checkContract(serverContractVersion: string | undefined): ContractCheck {
    const raw = (serverContractVersion ?? '').trim();
    const major = Number.parseInt(raw.split('.')[0] ?? '', 10);
    if (Number.isNaN(major)) {
        return { compatible: false, reason: `The mux server did not report a usable contract version ("${raw}").` };
    }

    if (major < SUPPORTED_CONTRACT_MAJOR) {
        return { compatible: false, reason: `The mux server is too old (contract ${raw}); update mux to use this extension.` };
    }

    if (major > SUPPORTED_CONTRACT_MAJOR) {
        return { compatible: false, reason: `This extension is too old for the mux server (contract ${raw}); update the extension.` };
    }

    return { compatible: true, reason: '' };
}

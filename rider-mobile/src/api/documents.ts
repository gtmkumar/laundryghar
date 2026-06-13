/**
 * Rider KYC document API — maps to the LIVE rider self-service endpoints:
 *
 *   GET  /api/v1/rider/documents
 *     → { kycStatus, vehicleVerificationStatus, vehicleRejectionReason, documents[] }
 *
 *   POST /api/v1/rider/documents   (multipart/form-data)
 *     fields: docType (one of the 5) + file (JPEG/PNG/WebP/PDF ≤5MB)
 *     → the created/updated RiderDocumentDto
 *     Re-uploading a docType replaces its file and resets status to "pending".
 *
 * Endpoint prefix: {Logistics}/api/v1/rider/ — RiderOnly policy (Bearer token,
 * user_type=rider). The rider identity is resolved from the JWT, never the path.
 *
 * Mirrors the proof-photo / inspection multipart approach (see api/inspection.ts):
 * RN/Expo FormData file fields expect { uri, name, type }.
 */
import axios from 'axios';
import { ApiError, logisticsClient, unwrapSingle } from '@/api/client';
import type {
  RiderDocType,
  RiderDocumentDto,
  RiderVerificationDto,
  SingleResponse,
} from '@/types/api';

function toApiError(e: unknown, fallback: string): ApiError {
  if (axios.isAxiosError(e) && e.response?.data) {
    const env = e.response.data as SingleResponse<unknown>;
    return new ApiError(env.message?.responseMessage ?? fallback, { status: false });
  }
  if (e instanceof ApiError) return e;
  return new ApiError(fallback);
}

/**
 * The picked file to upload. `uri` is a local file:// path from the image
 * picker / document picker; `mime` is the asset content type; `name` is a
 * real filename the server uses for the stored object + extension sniffing.
 */
export interface RiderDocumentFile {
  uri:  string;
  name: string;
  mime: string;
}

// ---------------------------------------------------------------------------
// GET /api/v1/rider/documents
// Overall KYC + vehicle verification state plus per-document review status.
// ---------------------------------------------------------------------------
export async function getMyVerification(): Promise<RiderVerificationDto> {
  try {
    const res = await logisticsClient.get<SingleResponse<RiderVerificationDto>>(
      '/rider/documents',
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load your verification status. Try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/rider/documents  (multipart/form-data)
// Uploads (or replaces) a document for the given docType. Returns the doc.
// ---------------------------------------------------------------------------
export async function uploadRiderDocument(
  docType: RiderDocType,
  file: RiderDocumentFile,
): Promise<RiderDocumentDto> {
  try {
    const form = new FormData();
    form.append('docType', docType);
    // RN/Expo FormData file part — { uri, name, type }. Carrying a real
    // filename + content-type lets the server sniff the extension and enforce
    // the JPEG/PNG/WebP/PDF allowlist.
    form.append('file', {
      uri:  file.uri,
      name: file.name,
      type: file.mime,
    } as unknown as Blob);

    const res = await logisticsClient.post<SingleResponse<RiderDocumentDto>>(
      '/rider/documents',
      form,
      {
        headers: { 'Content-Type': 'multipart/form-data' },
        // Documents (esp. PDFs / photos) can be large on slow links.
        timeout: 90_000,
      },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not upload the document. Try again.');
  }
}

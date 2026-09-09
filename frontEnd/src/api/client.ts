import { signRequestProof, type ClientProofHeaders } from "../auth/dpop";

const rawUrlAccess = import.meta.env.VITE_URL_ACCESS;
const rawGatewayUrl = import.meta.env.VITE_GATEWAY_URL;

function trimTrailingSlash(url: string): string {
	let clean = url;
	while (clean.endsWith("/")) {
		clean = clean.slice(0, -1);
	}
	return clean;
}

export const URL_ACCESS = trimTrailingSlash(rawUrlAccess);
export const GATEWAY_URL = trimTrailingSlash(rawGatewayUrl);
export const API_BASE = `${GATEWAY_URL}${URL_ACCESS}`;
export const AUTH_BASE = GATEWAY_URL;

export class ApiError extends Error {
	readonly status: number;
	readonly details: string | null;

	constructor(status: number, message: string, details: string | null = null) {
		super(message);
		this.name = "ApiError";
		this.status = status;
		this.details = details;
	}
}

interface ErrorBody {
	error?: string;
	message?: string;
	details?: string;
}

let unauthorizedHandler: (() => void) | null = null;

export function onUnauthorized(handler: (() => void) | null): void {
	unauthorizedHandler = handler;
}

async function toApiError(response: Response): Promise<ApiError> {
	let body: ErrorBody | null;
	try {
		body = (await response.json()) as ErrorBody;
	} catch {
		body = null;
	}
	const message = body?.error ?? body?.message ?? `${response.status} ${response.statusText}`;
	return new ApiError(response.status, message, body?.details ?? null);
}

export async function request<T>(path: string, init?: RequestInit, base: string = API_BASE): Promise<T> {
	let response: Response;
	const method = init?.method ?? "GET";
	const fullPath = path.startsWith("http") || (base.length > 0 && path.startsWith(base)) ? path : `${base}${path}`;

	let dpopHeaders: Partial<ClientProofHeaders> = {};
	if (typeof window !== "undefined") {
		try {
			dpopHeaders = await signRequestProof(method, fullPath);
		} catch {
			dpopHeaders = {};
		}
	}

	try {
		response = await fetch(fullPath, {
			...init,
			credentials: "include",
			headers: {
				Accept: "application/json",
				...(init?.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
				...dpopHeaders,
				...init?.headers,
			},
		});
	} catch (cause) {
		throw new ApiError(0, "No se pudo conectar con el servidor", String(cause));
	}

	if (response.status === 401) {
		unauthorizedHandler?.();
		throw await toApiError(response);
	}

	if (!response.ok) {
		throw await toApiError(response);
	}

	if (response.status === 204 || response.headers.get("content-length") === "0") {
		return undefined as T;
	}

	const text = await response.text();
	if (!text || text.trim().length === 0) {
		return undefined as T;
	}

	return JSON.parse(text) as T;
}

function makeClient(base: string) {
	return {
		get: <T>(path: string) => request<T>(path, undefined, base),
		post: <T>(path: string, body?: unknown) =>
			request<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }, base),
		put: <T>(path: string, body?: unknown) =>
			request<T>(path, { method: "PUT", body: body === undefined ? undefined : JSON.stringify(body) }, base),
		delete: <T>(path: string) => request<T>(path, { method: "DELETE" }, base),
		upload: <T>(path: string, form: FormData) => request<T>(path, { method: "POST", body: form }, base),
	};
}

export const http = makeClient(API_BASE);

/**
 * El Gateway sirve /auth/* en su raiz, no bajo el prefijo del plugin: mezclar
 * ambos manda el login a /diarspeicher/auth/login, donde nginx lo corta.
 */
export const gatewayHttp = makeClient(AUTH_BASE);

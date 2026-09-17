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
export const AUTH_BASE = `${GATEWAY_URL}/plugins/diarspeicher/auth`;

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

function resolveFullPath(path: string, base: string): string {
	if (path.startsWith("http")) {
		return path;
	}
	if (base.length > 0 && path.startsWith(base)) {
		return path;
	}
	return `${base}${path}`;
}

async function resolveDpopHeaders(method: string, fullPath: string): Promise<Partial<ClientProofHeaders>> {
	if (typeof window === "undefined") {
		return {};
	}
	try {
		return await signRequestProof(method, fullPath);
	} catch {
		return {};
	}
}

function handleFetchError(cause: unknown): never {
	if ((cause instanceof DOMException || cause instanceof Error) && cause.name === "AbortError") {
		throw cause;
	}
	throw new ApiError(0, "No se pudo conectar con el servidor", String(cause));
}

async function parseResponseBody<T>(response: Response): Promise<T> {
	if (response.status === 204 || response.headers.get("content-length") === "0") {
		return undefined as T;
	}

	const text = await response.text();
	if (!text || text.trim().length === 0) {
		return undefined as T;
	}

	return JSON.parse(text) as T;
}

function buildRequestHeaders(init?: RequestInit, dpopHeaders: Partial<ClientProofHeaders> = {}): Headers {
	const headers = new Headers(init?.headers);
	headers.set("Accept", "application/json");
	if (!(init?.body instanceof FormData) && !headers.has("Content-Type")) {
		headers.set("Content-Type", "application/json");
	}
	for (const [key, value] of Object.entries(dpopHeaders)) {
		if (value) {
			headers.set(key, value);
		}
	}
	return headers;
}

export async function request<T>(path: string, init?: RequestInit, base: string = API_BASE): Promise<T> {
	const method = init?.method ?? "GET";
	const fullPath = resolveFullPath(path, base);
	const dpopHeaders = await resolveDpopHeaders(method, fullPath);

	let response: Response;
	try {
		response = await fetch(fullPath, {
			...init,
			credentials: "include",
			headers: buildRequestHeaders(init, dpopHeaders),
		});
	} catch (cause) {
		handleFetchError(cause);
	}

	if (response.status === 401) {
		unauthorizedHandler?.();
		throw await toApiError(response);
	}

	if (!response.ok) {
		throw await toApiError(response);
	}

	return await parseResponseBody<T>(response);
}

export interface ClientOptions {
	signal?: AbortSignal;
}

function makeClient(base: string) {
	return {
		get: <T>(path: string, options?: ClientOptions) => request<T>(path, { signal: options?.signal }, base),
		post: <T>(path: string, body?: unknown, options?: ClientOptions) =>
			request<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body), signal: options?.signal }, base),
		put: <T>(path: string, body?: unknown, options?: ClientOptions) =>
			request<T>(path, { method: "PUT", body: body === undefined ? undefined : JSON.stringify(body), signal: options?.signal }, base),
		delete: <T>(path: string, options?: ClientOptions) => request<T>(path, { method: "DELETE", signal: options?.signal }, base),
		upload: <T>(path: string, form: FormData, options?: ClientOptions) => request<T>(path, { method: "POST", body: form, signal: options?.signal }, base),
	};
}

async function readAllChunks(
	reader: ReadableStreamDefaultReader<Uint8Array>,
	contentLength: number,
	onProgress?: (loaded: number, total: number) => void,
	signal?: AbortSignal
): Promise<ArrayBuffer> {
	const chunks: Uint8Array[] = [];
	let loaded = 0;

	while (true) {
		if (signal?.aborted) {
			await reader.cancel().catch(() => undefined);
			throw new DOMException("The user aborted a request.", "AbortError");
		}
		const { done, value } = await reader.read();
		if (done) {
			break;
		}
		if (value) {
			chunks.push(value);
			loaded += value.length;
			onProgress?.(loaded, contentLength);
		}
	}

	const allChunks = new Uint8Array(loaded);
	let position = 0;
	for (const chunk of chunks) {
		allChunks.set(chunk, position);
		position += chunk.length;
	}
	return allChunks.buffer;
}

export async function downloadBinary(
	path: string,
	onProgress?: (loaded: number, total: number) => void,
	signal?: AbortSignal
): Promise<ArrayBuffer> {
	const method = "GET";
	const fullPath = resolveFullPath(path, API_BASE);
	const dpopHeaders = await resolveDpopHeaders(method, fullPath);

	const response = await fetch(fullPath, {
		method,
		credentials: "include",
		signal,
		headers: {
			...dpopHeaders,
		},
	});

	if (response.status === 401) {
		unauthorizedHandler?.();
		throw await toApiError(response);
	}

	if (!response.ok) {
		throw await toApiError(response);
	}

	const contentLength = Number(response.headers.get("content-length")) || 0;
	if (!onProgress || !contentLength || !response.body) {
		return await response.arrayBuffer();
	}

	return await readAllChunks(response.body.getReader(), contentLength, onProgress, signal);
}

export const http = makeClient(API_BASE);

/**
 * El Gateway sirve /auth/* en su raiz, no bajo el prefijo del plugin: mezclar
 * ambos manda el login a /diarspeicher/auth/login, donde nginx lo corta.
 */
export const gatewayHttp = makeClient(AUTH_BASE);

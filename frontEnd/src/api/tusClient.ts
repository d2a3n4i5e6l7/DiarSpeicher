import { API_BASE } from "./client";
import { signRequestProof, type ClientProofHeaders } from "../auth/dpop";

export interface TusUploadOptions {
	file: File;
	libraryId: string;
	subpath?: string;
	chunkSize?: number; // Tamaño por chunk, por defecto 8 MB (8 * 1024 * 1024)
	onProgress?: (bytesUploaded: number, bytesTotal: number, percentage: number) => void;
	onSuccess?: (fileUrl: string) => void;
	onError?: (error: Error) => void;
}

export type TusUploadStatus = "idle" | "uploading" | "paused" | "completed" | "error";

function encodeMetadataValue(value: string): string {
	try {
		return btoa(unescape(encodeURIComponent(value)));
	} catch {
		return btoa(value);
	}
}

export class TusUpload {
	private readonly options: TusUploadOptions;
	private uploadUrl: string | null = null;
	private status: TusUploadStatus = "idle";
	private currentOffset = 0;
	private currentXhr: XMLHttpRequest | null = null;
	private isAborted = false;

	constructor(options: TusUploadOptions) {
		this.options = {
			...options,
			chunkSize: options.chunkSize || 8 * 1024 * 1024, // 8 MB
		};
	}

	public getStatus(): TusUploadStatus {
		return this.status;
	}

	public getOffset(): number {
		return this.currentOffset;
	}

	public getFileSize(): number {
		return this.options.file.size;
	}

	public async start(): Promise<void> {
		if (this.status === "completed") return;

		this.isAborted = false;
		this.status = "uploading";

		try {
			// 1. Si no tenemos uploadUrl, crear la subida
			if (!this.uploadUrl) {
				this.uploadUrl = await this.createUpload();
				this.currentOffset = 0;
			} else {
				// 2. Si ya teníamos uploadUrl, verificar offset actual con HEAD
				this.currentOffset = await this.getOffsetFromServer(this.uploadUrl);
			}

			// 3. Subir en bucle de chunks hasta completar
			await this.uploadChunks();
		} catch (err: unknown) {
			if (this.isAborted) {
				this.status = "paused";
				return;
			}
			this.status = "error";
			const error = err instanceof Error ? err : new Error("Error en la subida TUS");
			this.options.onError?.(error);
		}
	}

	public pause(): void {
		if (this.status !== "uploading") return;

		this.isAborted = true;
		this.status = "paused";
		if (this.currentXhr) {
			this.currentXhr.abort();
			this.currentXhr = null;
		}
	}

	public async cancel(): Promise<void> {
		this.isAborted = true;
		this.status = "idle";
		if (this.currentXhr) {
			this.currentXhr.abort();
			this.currentXhr = null;
		}

		if (this.uploadUrl) {
			try {
				await this.deleteUpload(this.uploadUrl);
			} catch {
				// Ignorar error al limpiar
			}
			this.uploadUrl = null;
		}
		this.currentOffset = 0;
	}

	private async createUpload(): Promise<string> {
		const fullUrl = `${API_BASE}/api/v2/files`;
		const metadata = [
			`filename ${encodeMetadataValue(this.options.file.name)}`,
			`libraryId ${encodeMetadataValue(this.options.libraryId)}`,
		];

		if (this.options.subpath) {
			metadata.push(`subpath ${encodeMetadataValue(this.options.subpath)}`);
		}

		const dpopHeaders = await this.getDPoPHeaders("POST", fullUrl);

		return new Promise((resolve, reject) => {
			const xhr = new XMLHttpRequest();
			xhr.open("POST", fullUrl);
			xhr.withCredentials = true;

			xhr.setRequestHeader("Tus-Resumable", "1.0.0");
			xhr.setRequestHeader("Upload-Length", this.options.file.size.toString());
			xhr.setRequestHeader("Upload-Metadata", metadata.join(","));

			for (const [key, value] of Object.entries(dpopHeaders)) {
				if (value) xhr.setRequestHeader(key, value);
			}

			xhr.onload = () => {
				if (xhr.status === 201) {
					const location = xhr.getResponseHeader("Location");
					if (location) {
						// Resolver ubicación absoluta si es relativa
						const resolved = location.startsWith("http") ? location : `${API_BASE}${location}`;
						resolve(resolved);
					} else {
						reject(new Error("El servidor no devolvió la cabecera Location para la subida TUS"));
					}
				} else {
					reject(new Error(`Fallo al iniciar subida TUS: HTTP ${xhr.status}`));
				}
			};

			xhr.onerror = () => reject(new Error("Error de red al iniciar la subida TUS"));
			xhr.send();
		});
	}

	private async getOffsetFromServer(uploadUrl: string): Promise<number> {
		const dpopHeaders = await this.getDPoPHeaders("HEAD", uploadUrl);

		return new Promise((resolve, reject) => {
			const xhr = new XMLHttpRequest();
			xhr.open("HEAD", uploadUrl);
			xhr.withCredentials = true;

			xhr.setRequestHeader("Tus-Resumable", "1.0.0");
			for (const [key, value] of Object.entries(dpopHeaders)) {
				if (value) xhr.setRequestHeader(key, value);
			}

			xhr.onload = () => {
				if (xhr.status >= 200 && xhr.status < 300) {
					const offsetHeader = xhr.getResponseHeader("Upload-Offset");
					const offset = offsetHeader ? Number.parseInt(offsetHeader, 10) : 0;
					resolve(Number.isNaN(offset) ? 0 : offset);
				} else {
					reject(new Error(`No se pudo verificar el offset TUS: HTTP ${xhr.status}`));
				}
			};

			xhr.onerror = () => reject(new Error("Error de red al consultar el offset de subida"));
			xhr.send();
		});
	}

	private async uploadChunks(): Promise<void> {
		const totalBytes = this.options.file.size;
		const chunkSize = this.options.chunkSize || 8 * 1024 * 1024;

		while (this.currentOffset < totalBytes && !this.isAborted) {
			const start = this.currentOffset;
			const end = Math.min(start + chunkSize, totalBytes);
			const chunkBlob = this.options.file.slice(start, end);

			await this.sendChunk(chunkBlob, start, totalBytes);
		}

		if (!this.isAborted && this.currentOffset >= totalBytes) {
			this.status = "completed";
			this.options.onProgress?.(totalBytes, totalBytes, 100);
			this.options.onSuccess?.(this.uploadUrl || "");
		}
	}

	private async sendChunk(chunk: Blob, chunkOffset: number, totalBytes: number): Promise<void> {
		if (!this.uploadUrl) throw new Error("Upload URL inexistente");

		const dpopHeaders = await this.getDPoPHeaders("PATCH", this.uploadUrl);

		return new Promise((resolve, reject) => {
			const xhr = new XMLHttpRequest();
			this.currentXhr = xhr;
			xhr.open("PATCH", this.uploadUrl!);
			xhr.withCredentials = true;

			xhr.setRequestHeader("Tus-Resumable", "1.0.0");
			xhr.setRequestHeader("Upload-Offset", chunkOffset.toString());
			xhr.setRequestHeader("Content-Type", "application/offset+octet-stream");

			for (const [key, value] of Object.entries(dpopHeaders)) {
				if (value) xhr.setRequestHeader(key, value);
			}

			xhr.upload.onprogress = (e) => {
				if (e.lengthComputable && !this.isAborted) {
					const bytesUploaded = chunkOffset + e.loaded;
					const percentage = Math.min(100, Math.round((bytesUploaded / totalBytes) * 100));
					this.options.onProgress?.(bytesUploaded, totalBytes, percentage);
				}
			};

			xhr.onload = () => {
				this.currentXhr = null;
				if (xhr.status === 204) {
					const offsetHeader = xhr.getResponseHeader("Upload-Offset");
					if (offsetHeader) {
						this.currentOffset = Number.parseInt(offsetHeader, 10);
					} else {
						this.currentOffset = chunkOffset + chunk.size;
					}
					resolve();
				} else if (xhr.status === 409) {
					// Desincronización de offset: actualizar offset desde el header y reintentar
					const offsetHeader = xhr.getResponseHeader("Upload-Offset");
					if (offsetHeader) {
						this.currentOffset = Number.parseInt(offsetHeader, 10);
					}
					resolve();
				} else {
					reject(new Error(`Error enviando bloque TUS: HTTP ${xhr.status}`));
				}
			};

			xhr.onerror = () => {
				this.currentXhr = null;
				reject(new Error("Error de red al transferir bloque del archivo"));
			};

			xhr.onabort = () => {
				this.currentXhr = null;
				resolve();
			};

			xhr.send(chunk);
		});
	}

	private async deleteUpload(uploadUrl: string): Promise<void> {
		const dpopHeaders = await this.getDPoPHeaders("DELETE", uploadUrl);

		return new Promise((resolve) => {
			const xhr = new XMLHttpRequest();
			xhr.open("DELETE", uploadUrl);
			xhr.withCredentials = true;
			xhr.setRequestHeader("Tus-Resumable", "1.0.0");

			for (const [key, value] of Object.entries(dpopHeaders)) {
				if (value) xhr.setRequestHeader(key, value);
			}

			xhr.onload = () => resolve();
			xhr.onerror = () => resolve();
			xhr.send();
		});
	}

	private async getDPoPHeaders(method: string, url: string): Promise<Partial<ClientProofHeaders>> {
		if (typeof window === "undefined") return {};
		try {
			return await signRequestProof(method, url);
		} catch {
			return {};
		}
	}
}

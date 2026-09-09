const DB_NAME = "gateway_crypto_db";
const STORE_NAME = "keys";
const KEY_NAME = "session_keypair";

let cachedKeyPair: CryptoKeyPair | null = null;

function openDb(): Promise<IDBDatabase> {
	return new Promise((resolve, reject) => {
		if (typeof window === "undefined" || !window.indexedDB) {
			reject(new Error("IndexedDB no está disponible en este entorno"));
			return;
		}
		const request = window.indexedDB.open(DB_NAME, 1);
		request.onupgradeneeded = () => {
			const db = request.result;
			if (!db.objectStoreNames.contains(STORE_NAME)) {
				db.createObjectStore(STORE_NAME);
			}
		};
		request.onsuccess = () => resolve(request.result);
		request.onerror = () => reject(new Error(request.error?.message || "Error al abrir IndexedDB"));
	});
}

async function loadKeyPairFromDb(): Promise<CryptoKeyPair | null> {
	try {
		const db = await openDb();
		return await new Promise((resolve, reject) => {
			const tx = db.transaction(STORE_NAME, "readonly");
			const store = tx.objectStore(STORE_NAME);
			const request = store.get(KEY_NAME);
			request.onsuccess = () => resolve((request.result as CryptoKeyPair) ?? null);
			request.onerror = () => reject(new Error(request.error?.message || "Error al leer clave de IndexedDB"));
		});
	} catch {
		return null;
	}
}

async function saveKeyPairToDb(keyPair: CryptoKeyPair): Promise<void> {
	try {
		const db = await openDb();
		await new Promise<void>((resolve, reject) => {
			const tx = db.transaction(STORE_NAME, "readwrite");
			const store = tx.objectStore(STORE_NAME);
			const request = store.put(keyPair, KEY_NAME);
			request.onsuccess = () => resolve();
			request.onerror = () => reject(new Error(request.error?.message || "Error al guardar clave en IndexedDB"));
		});
	} catch {
		cachedKeyPair = keyPair;
	}
}

/**
 * Obtiene el par de claves ECDSA P-256 no extraíbles o genera uno nuevo.
 * extractable: false asegura que la clave privada nunca pueda ser volcada
 * por scripts, extensiones o devtools.
 */
async function getOrCreateKeyPair(): Promise<CryptoKeyPair> {
	if (cachedKeyPair) {
		return cachedKeyPair;
	}

	const stored = await loadKeyPairFromDb();
	if (stored?.privateKey && stored?.publicKey) {
		cachedKeyPair = stored;
		return stored;
	}

	const generated = await window.crypto.subtle.generateKey(
		{
			name: "ECDSA",
			namedCurve: "P-256",
		},
		false,
		["sign"]
	);

	cachedKeyPair = generated;
	await saveKeyPairToDb(generated);
	return generated;
}

function arrayBufferToBase64(buffer: ArrayBuffer): string {
	const bytes = new Uint8Array(buffer);
	let binary = "";
	for (const byte of bytes) {
		binary += String.fromCodePoint(byte);
	}
	return btoa(binary);
}

/**
 * Exporta la clave pública en formato SPKI DER (Base64) para asociarla
 * a la sesión durante el login.
 */
export async function getPublicKeyBase64(): Promise<string> {
	const keyPair = await getOrCreateKeyPair();
	const spkiBuffer = await window.crypto.subtle.exportKey("spki", keyPair.publicKey);
	return arrayBufferToBase64(spkiBuffer);
}

export async function resetKeyPair(): Promise<void> {
	cachedKeyPair = null;
	try {
		const db = await openDb();
		await new Promise<void>((resolve) => {
			const tx = db.transaction(STORE_NAME, "readwrite");
			tx.objectStore(STORE_NAME).delete(KEY_NAME);
			tx.oncomplete = () => resolve();
			tx.onerror = () => resolve();
			tx.onabort = () => resolve();
		});
	} catch {
		cachedKeyPair = null;
	}
}

export interface ClientProofHeaders {
	DPoP: string;
}

function base64Url(bytes: ArrayBuffer | Uint8Array): string {
	const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
	let binary = "";
	for (const byte of view) binary += String.fromCodePoint(byte);
	return window.btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function encodeSegment(value: unknown): string {
	return base64Url(new TextEncoder().encode(JSON.stringify(value)));
}

/**
 * El orden de las claves del JWK es el del thumbprint de la RFC 7638 y el
 * gateway compara `x` e `y` contra la clave registrada en el login, asi que no
 * puede llevar campos extra como `ext` o `key_ops`.
 */
async function publicJwk(publicKey: CryptoKey): Promise<Record<string, string>> {
	const exported = await window.crypto.subtle.exportKey("jwk", publicKey);
	return { crv: "P-256", kty: "EC", x: exported.x ?? "", y: exported.y ?? "" };
}

export async function signRequestProof(method: string, path: string): Promise<ClientProofHeaders> {
	const keyPair = await getOrCreateKeyPair();
	/** JOSE (JSON Object Signing and Encryption): el encabezado del JWT, RFC 9449 §4.2. */
	const jose = {
		typ: "dpop+jwt",
		alg: "ES256",
		jwk: await publicJwk(keyPair.publicKey),
	};
	const claims = {
		jti: window.crypto.randomUUID(),
		htm: (method || "GET").toUpperCase(),
		htu: new URL(path.split("?")[0], window.location.origin).href,
		iat: Math.floor(Date.now() / 1000),
	};

	const signingInput = `${encodeSegment(jose)}.${encodeSegment(claims)}`;
	const signature = await window.crypto.subtle.sign(
		{
			name: "ECDSA",
			hash: { name: "SHA-256" },
		},
		keyPair.privateKey,
		new TextEncoder().encode(signingInput)
	);

	return { DPoP: `${signingInput}.${base64Url(signature)}` };
}

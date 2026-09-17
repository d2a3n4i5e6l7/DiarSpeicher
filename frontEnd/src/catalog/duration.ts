export function formatDurationSeconds(seconds: number): string {
	if (seconds < 60) {
		return `${String(Math.max(Math.floor(seconds), 1))} s`;
	}

	const minutes = Math.floor(seconds / 60);
	const rest = Math.floor(seconds % 60);
	if (minutes < 60) {
		return rest > 0 ? `${String(minutes)} min ${String(rest)} s` : `${String(minutes)} min`;
	}

	const hours = Math.floor(minutes / 60);
	return `${String(hours)} h ${String(minutes % 60)} min`;
}

export function formatDurationMinutes(minutes: number): string {
	if (minutes < 60) {
		return `${String(Math.floor(minutes))} MIN`;
	}
	const hours = Math.floor(minutes / 60);
	const rest = Math.floor(minutes % 60);
	return rest === 0 ? `${String(hours)} H` : `${String(hours)} H ${String(rest)} MIN`;
}

export function formatDuration(value: number, unit: "seconds" | "minutes" = "minutes"): string {
	return unit === "seconds" ? formatDurationSeconds(value) : formatDurationMinutes(value);
}

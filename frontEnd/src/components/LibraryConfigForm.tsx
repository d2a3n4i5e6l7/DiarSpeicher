import {
	Box,
	Divider,
	FormControl,
	FormControlLabel,
	InputLabel,
	MenuItem,
	Select,
	Stack,
	Switch,
	Typography,
} from "@mui/material";
import {
	LIBRARY_PATTERNS,
	LIBRARY_TYPES,
	READING_DIRECTIONS,
	READING_MODES,
	type LibraryConfig,
} from "../api/endpoints";

const TYPE_LABELS: Record<string, string> = {
	Comic: "Cómic",
	Manga: "Manga",
	Book: "Libro",
	LightNovel: "Novela ligera",
	Manhwa: "Manhwa",
	Mixed: "Mixta",
	WebNovel: "Novela web",
	Webtoon: "Webtoon",
};

const PATTERN_LABELS: Record<string, string> = {
	SeriesBased: "Por series (una carpeta por serie)",
	CollectionBased: "Por colecciones (carpetas anidadas)",
};

const DIRECTION_LABELS: Record<string, string> = {
	LeftToRight: "Izquierda a derecha",
	RightToLeft: "Derecha a izquierda (manga)",
};

const MODE_LABELS: Record<string, string> = {
	Paged: "Paginado",
	ContinuousVertical: "Tira vertical",
	ContinuousHorizontal: "Tira horizontal",
};

interface Props {
	value: LibraryConfig;
	onChange: (next: LibraryConfig) => void;
	disabled?: boolean;
}

export default function LibraryConfigForm({ value, onChange, disabled }: Readonly<Props>) {
	const set = <K extends keyof LibraryConfig>(key: K, next: LibraryConfig[K]) => {
		onChange({ ...value, [key]: next });
	};

	const toggle = (key: keyof LibraryConfig, label: string, helper: string) => (
		<Box>
			<FormControlLabel
				control={
					<Switch
						checked={Boolean(value[key])}
						onChange={(e) => set(key, e.target.checked as LibraryConfig[typeof key])}
						disabled={disabled}
					/>
				}
				label={label}
			/>
			<Typography variant="caption" color="text.secondary" sx={{ display: "block", ml: 6, mt: -0.5 }}>
				{helper}
			</Typography>
		</Box>
	);

	return (
		<Stack spacing={2.5}>
			<Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
				<FormControl fullWidth size="small" disabled={disabled}>
					<InputLabel>Tipo de contenido</InputLabel>
					<Select
						label="Tipo de contenido"
						value={value.libraryType}
						onChange={(e) => set("libraryType", e.target.value)}>
						{LIBRARY_TYPES.map((type) => (
							<MenuItem key={type} value={type}>
								{TYPE_LABELS[type] ?? type}
							</MenuItem>
						))}
					</Select>
				</FormControl>

				<FormControl fullWidth size="small" disabled={disabled}>
					<InputLabel>Estructura de carpetas</InputLabel>
					<Select
						label="Estructura de carpetas"
						value={value.libraryPattern}
						onChange={(e) => set("libraryPattern", e.target.value)}>
						{LIBRARY_PATTERNS.map((pattern) => (
							<MenuItem key={pattern} value={pattern}>
								{PATTERN_LABELS[pattern] ?? pattern}
							</MenuItem>
						))}
					</Select>
				</FormControl>
			</Stack>

			<Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
				<FormControl fullWidth size="small" disabled={disabled}>
					<InputLabel>Dirección de lectura</InputLabel>
					<Select
						label="Dirección de lectura"
						value={value.defaultReadingDir}
						onChange={(e) => set("defaultReadingDir", e.target.value)}>
						{READING_DIRECTIONS.map((dir) => (
							<MenuItem key={dir} value={dir}>
								{DIRECTION_LABELS[dir] ?? dir}
							</MenuItem>
						))}
					</Select>
				</FormControl>

				<FormControl fullWidth size="small" disabled={disabled}>
					<InputLabel>Modo de lectura</InputLabel>
					<Select
						label="Modo de lectura"
						value={value.defaultReadingMode}
						onChange={(e) => set("defaultReadingMode", e.target.value)}>
						{READING_MODES.map((mode) => (
							<MenuItem key={mode} value={mode}>
								{MODE_LABELS[mode] ?? mode}
							</MenuItem>
						))}
					</Select>
				</FormControl>
			</Stack>

			<Divider />

			<Stack spacing={1}>
				{toggle("processMetadata", "Leer metadatos embebidos", "Extrae ComicInfo.xml y metadatos de EPUB durante el escaneo.")}
				{toggle("generateFileHashes", "Calcular hash de fichero", "Identifica el archivo por contenido, no solo por ruta.")}
				{toggle("generateKoreaderHashes", "Calcular hash de KOReader", "Necesario para que KOReader sincronice el progreso.")}
				{toggle("convertRarToZip", "Convertir CBR a CBZ", "Los CBR requieren descompresión externa; el CBZ se sirve directo.")}
				{toggle("hardDeleteConversions", "Borrar el CBR tras convertir", "Solo se aplica si la conversión está activada.")}
			</Stack>
		</Stack>
	);
}

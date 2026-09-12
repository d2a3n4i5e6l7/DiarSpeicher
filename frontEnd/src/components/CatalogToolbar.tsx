import { Box, InputAdornment, MenuItem, Select, TextField, ToggleButton, Tooltip } from "@mui/material";
import SearchIcon from "@mui/icons-material/Search";
import ArrowUpwardIcon from "@mui/icons-material/ArrowUpward";
import ArrowDownwardIcon from "@mui/icons-material/ArrowDownward";
import ClearIcon from "@mui/icons-material/Close";
import { IconButton } from "@mui/material";
import { DS } from "../theme";

export type SortDirection = "asc" | "desc";

export interface SortOption {
	id: string;
	label: string;
	/** Orden natural del criterio: los textos ascienden, las fechas y contadores descienden. */
	defaultDirection: SortDirection;
}

interface Props {
	query: string;
	onQueryChange: (next: string) => void;
	sort: string;
	onSortChange: (next: string) => void;
	direction: SortDirection;
	onDirectionChange: (next: SortDirection) => void;
	options: readonly SortOption[];
	placeholder?: string;
	/** Resultados visibles tras filtrar, para que se vea que el filtro hizo algo. */
	shown?: number;
	total?: number;
}

/** Barra de búsqueda y ordenación. La comparten la rejilla de bibliotecas y la de series. */
export default function CatalogToolbar({
	query,
	onQueryChange,
	sort,
	onSortChange,
	direction,
	onDirectionChange,
	options,
	placeholder = "Buscar...",
	shown,
	total,
}: Readonly<Props>) {
	const flipped = direction === "desc";
	const showCount = shown !== undefined && total !== undefined;

	return (
		<Box
			sx={{
				display: "flex",
				flexWrap: "wrap",
				alignItems: "center",
				gap: 1.5,
				mb: 3,
				p: 1.5,
				backgroundColor: DS.bgSunken,
				border: `1px solid ${DS.border}`,
				clipPath: "polygon(10px 0%, 100% 0%, 100% calc(100% - 10px), calc(100% - 10px) 100%, 0% 100%, 0% 10px)",
			}}
		>
			<TextField
				value={query}
				onChange={(e) => onQueryChange(e.target.value)}
				placeholder={placeholder}
				size="small"
				sx={{ flexGrow: 1, minWidth: 200 }}
				slotProps={{
					input: {
						startAdornment: (
							<InputAdornment position="start">
								<SearchIcon sx={{ fontSize: 18, color: DS.muted }} />
							</InputAdornment>
						),
						endAdornment: query ? (
							<InputAdornment position="end">
								<IconButton size="small" aria-label="Limpiar búsqueda" onClick={() => onQueryChange("")}>
									<ClearIcon sx={{ fontSize: 16, color: DS.muted }} />
								</IconButton>
							</InputAdornment>
						) : null,
						sx: { fontFamily: "'Inter', sans-serif", fontSize: "13px" },
					},
				}}
			/>

			<Select
				value={sort}
				size="small"
				onChange={(e) => {
					const next = options.find((option) => option.id === e.target.value);
					onSortChange(e.target.value);
					// Cambiar de criterio reinicia el sentido al natural del nuevo:
					// nadie quiere "más antiguo primero" al pasar de nombre a fecha.
					if (next) onDirectionChange(next.defaultDirection);
				}}
				sx={{
					minWidth: 200,
					fontFamily: "'Rajdhani', sans-serif",
					fontWeight: 700,
					letterSpacing: "0.5px",
					fontSize: "13px",
				}}
			>
				{options.map((option) => (
					<MenuItem key={option.id} value={option.id} sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 600 }}>
						{option.label}
					</MenuItem>
				))}
			</Select>

			<Tooltip title={flipped ? "Orden descendente" : "Orden ascendente"}>
				<ToggleButton
					value="direction"
					selected={flipped}
					size="small"
					onChange={() => onDirectionChange(flipped ? "asc" : "desc")}
					sx={{
						borderRadius: 0,
						border: `1px solid ${DS.border}`,
						color: DS.muted,
						px: 1,
						"&.Mui-selected": { backgroundColor: "rgba(194, 24, 24, 0.18)", borderColor: DS.red, color: "#FFFFFF" },
					}}
				>
					{flipped ? <ArrowDownwardIcon sx={{ fontSize: 18 }} /> : <ArrowUpwardIcon sx={{ fontSize: 18 }} />}
				</ToggleButton>
			</Tooltip>

			{showCount && (
				<Box component="span" className="ds-pill-mono">
					{shown === total ? String(total) : `${String(shown)} / ${String(total)}`}
				</Box>
			)}
		</Box>
	);
}

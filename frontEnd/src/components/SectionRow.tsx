import { Box, Button, IconButton, Typography } from "@mui/material";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import { Link as RouterLink } from "react-router-dom";
import { useRef, type ReactNode } from "react";
import { DS } from "../theme";

interface Props {
	title: string;
	/** Contador monoespaciado junto al título (nº de elementos de la sección). */
	count?: number;
	moreTo?: string;
	children: ReactNode;
	empty?: string;
}

const SCROLL_STEP = 480;

/** Título de sección y carrusel horizontal. Lo comparten la home y la ficha de serie. */
export default function SectionRow({ title, count, moreTo, children, empty }: Readonly<Props>) {
	const trackRef = useRef<HTMLDivElement | null>(null);

	const scrollBy = (delta: number) => {
		trackRef.current?.scrollBy({ left: delta, behavior: "smooth" });
	};

	return (
		<Box sx={{ mb: 4 }}>
			<Box sx={{ display: "flex", alignItems: "center", gap: 1.5, mb: 1.5 }}>
				<Typography
					component="h2"
					sx={{
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "20px",
						fontWeight: 700,
						letterSpacing: "2px",
						textTransform: "uppercase",
						color: "#FFFFFF",
					}}
				>
					{title}
				</Typography>

				{count !== undefined && (
					<Box component="span" className="ds-pill-mono">
						{String(count).padStart(2, "0")}
					</Box>
				)}

				<Box
					sx={{
						flex: 1,
						height: "1px",
						background: "linear-gradient(to right, #C21818 0%, rgba(194, 24, 24, 0.1) 70%, transparent 100%)",
					}}
				/>

				<Box sx={{ display: { xs: "none", sm: "flex" }, gap: 0.5 }}>
					<IconButton
						size="small"
						aria-label="Desplazar a la izquierda"
						onClick={() => scrollBy(-SCROLL_STEP)}
						sx={{ color: DS.muted, border: `1px solid ${DS.border}`, borderRadius: 0, "&:hover": { color: "#FFFFFF", borderColor: DS.red } }}
					>
						<ChevronLeftIcon fontSize="small" />
					</IconButton>
					<IconButton
						size="small"
						aria-label="Desplazar a la derecha"
						onClick={() => scrollBy(SCROLL_STEP)}
						sx={{ color: DS.muted, border: `1px solid ${DS.border}`, borderRadius: 0, "&:hover": { color: "#FFFFFF", borderColor: DS.red } }}
					>
						<ChevronRightIcon fontSize="small" />
					</IconButton>
				</Box>

				{moreTo && (
					<Button component={RouterLink} to={moreTo} size="small" sx={{ color: DS.redGlow, flexShrink: 0 }}>
						VER TODO
					</Button>
				)}
			</Box>

			{empty ? (
				<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.subtle, py: 3 }}>
					{empty}
				</Typography>
			) : (
				<Box
					ref={trackRef}
					sx={{
						display: "flex",
						gap: 2,
						overflowX: "auto",
						overflowY: "hidden",
						pb: 1.5,
						scrollSnapType: "x proximity",
						"& > *": { scrollSnapAlign: "start" },
					}}
				>
					{children}
				</Box>
			)}
		</Box>
	);
}

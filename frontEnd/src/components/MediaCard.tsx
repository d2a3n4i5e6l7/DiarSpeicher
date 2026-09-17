import { Box, LinearProgress, Typography } from "@mui/material";
import { Link as RouterLink } from "react-router-dom";
import BrokenImageIcon from "@mui/icons-material/BrokenImage";
import { memo, useState } from "react";
import { DS } from "../theme";

interface Props {
	to: string;
	title: string;
	subtitle?: string;
	coverUrl: string;
	/** 0-100. Ausente o 0 no dibuja barra; 100 marca la tarjeta como terminada. */
	progress?: number;
	/** Etiqueta monoespaciada sobre la portada (nº de tomo, formato, páginas). */
	badge?: string;
	/** Ancho fijo del carrusel, o "100%" para que la rejilla mande. */
	width?: number | string;
	/** Barre la portada mientras el escaner esta leyendo justo esta serie. */
	scanning?: boolean;
	/** Estado de navegación que se preserva en RouterLink. */
	state?: unknown;
}

/**
 * Tarjeta de portada del catálogo. La relación 2:3 se reserva antes de que llegue la
 * imagen: sin ella la rejilla salta al cargar cada miniatura.
 */
function MediaCard({
	to,
	title,
	subtitle,
	coverUrl,
	progress,
	badge,
	width = 160,
	scanning = false,
	state,
}: Readonly<Props>) {
	const [failed, setFailed] = useState(false);
	const pct = Math.max(0, Math.min(100, progress ?? 0));

	return (
		<Box
			component={RouterLink}
			to={to}
			state={state}
			sx={{
				width,
				flexShrink: 0,
				textDecoration: "none",
				display: "block",
				"&:hover .ds-cover": {
					backgroundImage: DS.bevelMetalHot,
					boxShadow: "0 10px 30px rgba(var(--ds-red-rgb), var(--ds-glow-a))",
				},
				"&:hover .ds-cover-title": { color: DS.redLight },
			}}
		>
			<Box
				className="ds-cover"
				sx={{
					position: "relative",
					aspectRatio: "2 / 3",
					width: "100%",
					backgroundColor: "var(--ds-metal-lo)",
					backgroundImage: DS.bevelMetal,
					clipPath: "polygon(12px 0%, 100% 0%, 100% calc(100% - 12px), calc(100% - 12px) 100%, 0% 100%, 0% 12px)",
					transition: "background-image 0.25s ease, box-shadow 0.25s ease",
				}}
			>
				<Box
					sx={{
						position: "absolute",
						inset: "1px",
						clipPath: "polygon(11px 0%, 100% 0%, 100% calc(100% - 11px), calc(100% - 11px) 100%, 0% 100%, 0% 11px)",
						backgroundColor: DS.bgSunken,
						overflow: "hidden",
						display: "flex",
						alignItems: "center",
						justifyContent: "center",
					}}
				>
					{failed ? (
						<BrokenImageIcon sx={{ fontSize: 32, color: DS.borderRed }} />
					) : (
						<Box
							component="img"
							src={coverUrl}
							alt=""
							loading="lazy"
							onError={() => setFailed(true)}
							sx={{ width: "100%", height: "100%", objectFit: "cover", display: "block" }}
						/>
					)}
				</Box>

				{scanning && <Box className="ds-scanline" />}

				<Box className="hud-corner-tl" />
				<Box className="hud-corner-br" />

				{badge && (
					<Box
						component="span"
						className="ds-pill-mono"
						sx={{ position: "absolute", top: 8, right: 8, zIndex: 2 }}
					>
						{badge}
					</Box>
				)}

				{pct > 0 && (
					<LinearProgress
						variant="determinate"
						value={pct}
						sx={{
							position: "absolute",
							bottom: 0,
							left: 0,
							right: 0,
							height: 3,
							zIndex: 2,
							"& .MuiLinearProgress-bar": { backgroundColor: pct >= 100 ? "var(--ds-ok)" : DS.redGlow },
						}}
					/>
				)}
			</Box>

			{title && (
				<Typography
					className="ds-cover-title"
					sx={{
						mt: 1,
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "14px",
						fontWeight: 700,
						letterSpacing: "0.5px",
						textTransform: "uppercase",
						color: DS.platinum,
						transition: "color 0.2s ease",
						display: "-webkit-box",
						WebkitLineClamp: 2,
						WebkitBoxOrient: "vertical",
						overflow: "hidden",
						lineHeight: 1.25,
					}}
				>
					{title}
				</Typography>
			)}

			{subtitle && (
				<Typography
					sx={{
						fontFamily: "'JetBrains Mono', monospace",
						fontSize: "10px",
						letterSpacing: "0.5px",
						color: DS.subtle,
						mt: 0.25,
						overflow: "hidden",
						textOverflow: "ellipsis",
						whiteSpace: "nowrap",
					}}
				>
					{subtitle}
				</Typography>
			)}
		</Box>
	);
}

export default memo(MediaCard);

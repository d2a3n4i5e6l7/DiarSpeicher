import { Box, Typography } from "@mui/material";

interface DiarSpeicherLogoProps {
	size?: number;
	showText?: boolean;
	showSubtitle?: boolean;
	subtitle?: string;
}

export default function DiarSpeicherLogo({
	size = 36,
	showText = true,
	showSubtitle = false,
	subtitle = "ARCHIVE NODE",
}: Readonly<DiarSpeicherLogoProps>) {
	const icon = (
		<svg
			viewBox="0 0 200 200"
			width={size}
			height={size}
			style={{
				filter:
					"drop-shadow(0 0 16px rgba(var(--ds-red-rgb), var(--ds-glow-a)))",
				display: "block",
				flexShrink: 0,
			}}
		>
			<defs>
				<linearGradient id="dsMetal" x1="0" y1="0" x2="1" y2="1">
					<stop offset="0%" style={{ stopColor: "var(--ds-metal)" }} />
					<stop offset="50%" style={{ stopColor: "var(--ds-metal-2)" }} />
					<stop offset="100%" style={{ stopColor: "var(--ds-metal-lo)" }} />
				</linearGradient>
				<linearGradient id="dsCrimson" x1="0" y1="0" x2="1" y2="0">
					<stop offset="0%" style={{ stopColor: "var(--ds-red-hover)" }} />
					<stop offset="50%" style={{ stopColor: "var(--ds-red)" }} />
					<stop offset="100%" style={{ stopColor: "var(--ds-red-light)" }} />
				</linearGradient>
			</defs>
			{/* Cuerpo D blindado facetado */}
			<polygon
				points="40,20 135,20 185,75 185,125 135,180 40,180 40,150 55,135 55,65 40,50"
				fill="url(#dsMetal)"
				style={{ stroke: "var(--ds-metal-hi)" }}
				strokeWidth="3"
			/>
			{/* Acentos balísticos carmesí en vértices */}
			<polygon points="135,20 155,5 142,20" style={{ fill: "var(--ds-red)" }} />
			<polygon
				points="185,75 200,68 185,87"
				style={{ fill: "var(--ds-red)" }}
			/>
			<polygon
				points="185,125 200,132 185,113"
				style={{ fill: "var(--ds-red)" }}
			/>
			<polygon
				points="135,180 155,195 142,180"
				style={{ fill: "var(--ds-red)" }}
			/>
			{/* Núcleo interior oscuro */}
			<polygon
				points="80,55 115,55 150,90 150,110 115,145 80,145"
				style={{ fill: "var(--ds-bg-deep)", stroke: "var(--ds-red-hover)" }}
				strokeWidth="2"
			/>
			{/* Cruz de hierro carmesí central de Iron Blood */}
			<g transform="translate(118, 100)">
				<polygon points="0,-42 7,-18 0,-10 -7,-18" fill="url(#dsCrimson)" />
				<polygon points="0,42 7,18 0,10 -7,18" fill="url(#dsCrimson)" />
				<polygon points="-35,0 -14,6 -8,0 -14,-6" fill="url(#dsCrimson)" />
				<polygon points="35,0 14,6 8,0 14,-6" fill="url(#dsCrimson)" />
				<polygon
					points="0,-7 7,0 0,7 -7,0"
					fill="#FFF"
					style={{ stroke: "var(--ds-red-glow)" }}
					strokeWidth="1.5"
				/>
			</g>
		</svg>
	);

	if (!showText) {
		return icon;
	}

	return (
		<Box sx={{ display: "inline-flex", alignItems: "center", gap: 1.5 }}>
			{icon}
			<Box sx={{ display: "flex", flexDirection: "column" }}>
				<Typography
					component="span"
					sx={{
						fontFamily: "'Orbitron', sans-serif",
						fontWeight: 900,
						fontSize: size > 40 ? 24 : 17,
						letterSpacing: "2px",
						lineHeight: 1.1,
						color: "var(--ds-text-strong)",
					}}
				>
					DIAR
					<Box component="span" sx={{ color: "var(--ds-red-glow)" }}>
						SPEICHER
					</Box>
				</Typography>
				{showSubtitle && (
					<Typography
						component="span"
						sx={{
							fontFamily: "'Rajdhani', sans-serif",
							fontWeight: 700,
							fontSize: 10,
							letterSpacing: "3px",
							color: "var(--ds-muted)",
							textTransform: "uppercase",
							mt: 0.25,
						}}
					>
						{subtitle}
					</Typography>
				)}
			</Box>
		</Box>
	);
}

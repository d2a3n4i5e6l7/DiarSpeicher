import {
	Alert,
	Box,
	Button,
	Chip,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Stack,
	Typography,
} from "@mui/material";
import { useEffect, useState } from "react";
import HudFrame from "./HudFrame";
import { metadataApi, type MangaBakaCandidate } from "../api/endpoints";
import { DS } from "../theme";

interface Result {
	key: string;
	candidates: MangaBakaCandidate[];
	error?: string;
}

interface Props {
	open: boolean;
	seriesId: string;
	seriesName: string;
	onClose: () => void;
	onMatched: () => void;
}

/**
 * Elige entre los candidatos del volcado.
 *
 * Nunca empareja solo: "Galaxy Angel" devuelve cinco obras distintas —la original, Beta,
 * Party, II y 2nd— y acertar por parecido de título escribiría la ficha equivocada. Por eso
 * se enseñan portada, año y tipo, que es lo que permite a una persona distinguirlas.
 */
export default function MetadataMatchDialog({ open, seriesId, seriesName, onClose, onMatched }: Readonly<Props>) {
	// El resultado se guarda junto a la serie que lo pidió: al abrir el diálogo para otra
	// serie, lo anterior deja de estar vigente sin tener que vaciarlo desde el efecto.
	const [result, setResult] = useState<Result | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [applying, setApplying] = useState<number | null>(null);

	useEffect(() => {
		if (!open) return;

		let mounted = true;
		metadataApi
			.candidates(seriesId, 12)
			.then((found) => {
				if (mounted) setResult({ key: seriesId, candidates: found });
			})
			.catch((err: unknown) => {
				if (mounted) {
					setResult({
						key: seriesId,
						candidates: [],
						error: err instanceof Error ? err.message : "No se pudo consultar el catálogo externo.",
					});
				}
			});

		return () => {
			mounted = false;
		};
	}, [open, seriesId]);

	const fresh = result?.key === seriesId ? result : null;
	const candidates = fresh?.candidates ?? null;
	const loadError = error ?? fresh?.error ?? null;

	const apply = async (candidate: MangaBakaCandidate) => {
		setApplying(candidate.id);
		setError(null);
		try {
			await metadataApi.match(seriesId, candidate.id);
			onMatched();
			onClose();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo aplicar el emparejado.");
		} finally {
			setApplying(null);
		}
	};

	return (
		<Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
			<HudFrame />
			<DialogTitle>Emparejar: {seriesName}</DialogTitle>

			<DialogContent sx={{ p: 3 }}>
				{loadError && (
					<Alert severity="error" sx={{ mb: 2 }}>
						{loadError}
					</Alert>
				)}

				{candidates === null && (
					<Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
						<CircularProgress size={28} thickness={5} />
					</Box>
				)}

				{candidates?.length === 0 && (
					<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.muted, py: 3 }}>
						Ningún candidato. Puede que el volcado no esté descargado todavía, o que el nombre de la carpeta no se
						parezca al título de la obra.
					</Typography>
				)}

				<Stack spacing={1.5}>
					{candidates?.map((candidate) => (
						<Box
							key={candidate.id}
							sx={{
								display: "flex",
								gap: 2,
								p: 1.5,
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								transition: "border-color 0.2s ease",
								"&:hover": { borderColor: DS.red },
							}}
						>
							<Box
								component="span"
								className="ds-pill-mono"
								sx={{ flexShrink: 0, alignSelf: "flex-start" }}
							>
								#{String(candidate.id)}
							</Box>

							<Box sx={{ flexGrow: 1, minWidth: 0 }}>
								<Typography
									sx={{
										fontFamily: "'Rajdhani', sans-serif",
										fontSize: "16px",
										fontWeight: 700,
										letterSpacing: "0.5px",
										color: "#FFFFFF",
									}}
								>
									{candidate.title}
								</Typography>
								{candidate.nativeTitle && (
									<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "12px", color: DS.subtle }}>
										{candidate.nativeTitle}
									</Typography>
								)}
								<Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 0.75, mt: 1 }}>
									{candidate.type && <Chip size="small" variant="outlined" label={candidate.type.toUpperCase()} />}
									{candidate.year && <Chip size="small" variant="outlined" label={String(candidate.year)} />}
									{candidate.status && <Chip size="small" variant="outlined" label={candidate.status.toUpperCase()} />}
									{typeof candidate.rating === "number" && (
										<Chip size="small" variant="outlined" label={`★ ${candidate.rating.toFixed(1)}`} />
									)}
								</Stack>

								{candidate.authors && (
									<Typography
										sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: DS.redLight, mt: 1 }}
									>
										{candidate.authors.toUpperCase()}
									</Typography>
								)}

								{candidate.genres && (
									<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: DS.subtle, mt: 0.5 }}>
										{candidate.genres}
									</Typography>
								)}

								{candidate.description && (
									<Typography
										sx={{
											fontFamily: "'Inter', sans-serif",
											fontSize: "12px",
											color: "#A3ABB8",
											lineHeight: 1.6,
											mt: 1,
											display: "-webkit-box",
											WebkitLineClamp: 3,
											WebkitBoxOrient: "vertical",
											overflow: "hidden",
										}}
									>
										{candidate.description}
									</Typography>
								)}
							</Box>

							<Button
								variant="contained"
								disabled={applying !== null}
								onClick={() => {
									void apply(candidate);
								}}
								sx={{ alignSelf: "center", flexShrink: 0, backgroundColor: DS.red }}
							>
								{applying === candidate.id ? "APLICANDO..." : "ES ESTA"}
							</Button>
						</Box>
					))}
				</Stack>
			</DialogContent>

			<DialogActions>
				<Button onClick={onClose} sx={{ color: DS.muted }}>
					CANCELAR
				</Button>
			</DialogActions>
		</Dialog>
	);
}

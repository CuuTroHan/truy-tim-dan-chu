window.ttdcDownloadBytes = (fileName, base64) => {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const blob = new Blob([bytes], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob); const a = document.createElement("a");
  a.href = url; a.download = fileName || "match-results.csv"; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};

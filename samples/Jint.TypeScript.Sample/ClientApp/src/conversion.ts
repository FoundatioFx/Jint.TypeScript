import ConversionWorker from './conversion.worker.mjs?worker';

export type ConversionResult = { success: true; files: Record<string, string>; file: string }
    | { success: false; issues: { message: string; line?: number; column?: number }[] };

export function convertJsDoc(files: Record<string, string>, file: string): Promise<ConversionResult> {
    // Spawn only on demand; terminate after every attempt, including a hard timeout.
    const worker = new ConversionWorker();
    return new Promise(resolve => {
        const finish = (result: ConversionResult) => { clearTimeout(timeout); worker.terminate(); resolve(result); };
        const timeout = setTimeout(() => finish({ success: false, issues: [{ message: 'Conversion took too long. Your files have not changed.' }] }), 10_000);
        worker.onmessage = ({ data }: MessageEvent<ConversionResult>) => finish(data);
        worker.onerror = event => { event.preventDefault(); finish({ success: false, issues: [{ message: 'The conversion worker could not start. Your files have not changed.' }] }); };
        worker.postMessage({ files, file });
    });
}

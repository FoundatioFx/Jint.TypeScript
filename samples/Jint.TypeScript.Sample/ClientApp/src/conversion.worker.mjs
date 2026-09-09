import { convertJsDoc } from './jsdoc-converter.mjs';
self.onmessage = async ({ data }) => self.postMessage(await convertJsDoc(data.files, data.file));

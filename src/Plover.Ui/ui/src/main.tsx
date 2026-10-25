import { browserServices } from "./services/browserServices";
import { start } from "./start";

start(browserServices, document.getElementById("root")!);

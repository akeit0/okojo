import defaultValue, { source as importedValue } from "dependency";
import * as namespaceValue from "namespace";

export { defaultValue as forwardedDefault };
export { importedValue as forwardedValue };
export { namespaceValue as forwardedNamespace };

const localValue = 1;
export { localValue, localValue as localAlias };
export default 2;

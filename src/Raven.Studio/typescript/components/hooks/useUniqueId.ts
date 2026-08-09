import { useState } from "react";

const useUniqueId = (prefix: string) => {
    const [id] = useState(() => _.uniqueId(prefix));

    return id;
};

export default useUniqueId;

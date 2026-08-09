import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useAppSelector } from "components/store";
import { editGenAiTaskSelectors } from "../store/editGenAiTaskSlice";
import router from "plugins/router";
import { useAppUrls } from "components/hooks/useAppUrls";

export default function useEditGenAiCancel() {
    const sourceView = useAppSelector(editGenAiTaskSelectors.sourceView);
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);

    const { appUrl } = useAppUrls();

    return () => {
        if (sourceView === "AiTasks") {
            router.navigate(appUrl.forAiTasks(databaseName));
        } else {
            router.navigate(appUrl.forOngoingTasks(databaseName));
        }
    };
}

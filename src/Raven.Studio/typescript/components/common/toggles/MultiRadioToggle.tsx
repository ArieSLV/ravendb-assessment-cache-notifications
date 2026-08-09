import "./Toggles.scss";
import useUniqueId from "components/hooks/useUniqueId";
import classNames from "classnames";
import { InputItem } from "components/models/common";
import ToggleItemLabel from "components/common/toggles/partials/ToggleItemLabel";

interface MultiRadioToggleProps<T extends string | number = string> {
    inputItems: InputItem<T>[];
    selectedItem: T;
    setSelectedItem: (x: T) => void;
    className?: string;
    label?: string;
}

export function MultiRadioToggle<T extends string | number = string>({
    inputItems,
    selectedItem,
    setSelectedItem,
    className,
    label,
}: MultiRadioToggleProps<T>) {
    const uniqueId = useUniqueId("multi-radio-toggle");

    return (
        <div className={classNames("multi-toggle", className)}>
            {label && <div className="small-label ms-1 mb-1">{label}</div>}
            <div className="multi-toggle-list">
                {inputItems.map((inputItem) => (
                    <Item
                        key={uniqueId + inputItem.value}
                        id={uniqueId + inputItem.value}
                        inputItem={inputItem}
                        selectedItem={selectedItem}
                        setSelectedItem={setSelectedItem}
                    />
                ))}
            </div>
        </div>
    );
}

interface ItemProps<T extends string | number = string> {
    id: string;
    inputItem: InputItem<T>;
    selectedItem: T;
    setSelectedItem: (item: T) => void;
}

function Item<T extends string | number = string>({ id, inputItem, selectedItem, setSelectedItem }: ItemProps<T>) {
    return (
        <div className="flex-horizontal">
            {inputItem.verticalSeparatorLine && <div className="vr" />}
            <div className="multi-toggle-item">
                <input
                    id={id}
                    type="radio"
                    name={id}
                    checked={inputItem.value === selectedItem}
                    onChange={() => setSelectedItem(inputItem.value)}
                />
                <ToggleItemLabel id={id} inputItem={inputItem} />
            </div>
        </div>
    );
}

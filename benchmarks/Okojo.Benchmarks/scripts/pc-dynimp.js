function makeNestedExpressionHeavy() {
    function loadOne(path) {
        return path + ".js";
    }

    return function run(flag) {
        return flag
            ? function nestedA() {
                return loadOne("./a");
            }
            : function nestedB() {
                function deeper(path) {
                    return loadOne(path);
                }

                return deeper("./b");
            };
    };
}

makeNestedExpressionHeavy(true);

const holder = {
    async read(value) {
        return await value;
    },
};

holder.read(Promise.resolve(1));

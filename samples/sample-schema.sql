CREATE SCHEMA app;

CREATE TYPE app.user_status AS ENUM (
    'active',
    'blocked'
);

CREATE SEQUENCE app.users_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE app.users (
    id integer DEFAULT nextval('app.users_id_seq'::regclass) NOT NULL,
    email text NOT NULL,
    status app.user_status DEFAULT 'active'::app.user_status NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL
);

ALTER TABLE ONLY app.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);

CREATE UNIQUE INDEX users_email_idx ON app.users USING btree (email);

CREATE VIEW app.active_users AS
 SELECT users.id,
    users.email
   FROM app.users
  WHERE (users.status = 'active'::app.user_status);

CREATE FUNCTION app.normalize_email(value text) RETURNS text
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN lower(trim(value));
END;
$$;

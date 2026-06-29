--
-- PostgreSQL database dump
--

-- Dumped from database version 17.5
-- Dumped by pg_dump version 17.5

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: app; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA app;


--
-- Name: user_status; Type: TYPE; Schema: app; Owner: -
--

CREATE TYPE app.user_status AS ENUM (
    'active',
    'blocked',
    'deleted'
);


--
-- Name: calculate_order_total(bigint); Type: FUNCTION; Schema: app; Owner: -
--

CREATE FUNCTION app.calculate_order_total(order_id_value bigint) RETURNS numeric
    LANGUAGE sql
    AS $$
    SELECT COALESCE(SUM(quantity * unit_price), 0)
    FROM app.order_items
    WHERE order_id = order_id_value;
$$;


--
-- Name: normalize_email(text); Type: FUNCTION; Schema: app; Owner: -
--

CREATE FUNCTION app.normalize_email(value text) RETURNS text
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN lower(trim(value));
END;
$$;


--
-- Name: set_normalized_email(); Type: FUNCTION; Schema: app; Owner: -
--

CREATE FUNCTION app.set_normalized_email() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.email := app.normalize_email(NEW.email);
    RETURN NEW;
END;
$$;


--
-- Name: users_id_seq; Type: SEQUENCE; Schema: app; Owner: -
--

CREATE SEQUENCE app.users_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: users; Type: TABLE; Schema: app; Owner: -
--

CREATE TABLE app.users (
    id integer DEFAULT nextval('app.users_id_seq'::regclass) NOT NULL,
    email text NOT NULL,
    full_name text NOT NULL,
    status app.user_status DEFAULT 'active'::app.user_status NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: active_users; Type: VIEW; Schema: app; Owner: -
--

CREATE VIEW app.active_users AS
 SELECT id,
    email,
    full_name,
    created_at
   FROM app.users u
  WHERE (status = 'active'::app.user_status);


--
-- Name: order_items; Type: TABLE; Schema: app; Owner: -
--

CREATE TABLE app.order_items (
    id bigint NOT NULL,
    order_id bigint NOT NULL,
    product_name text NOT NULL,
    quantity integer NOT NULL,
    unit_price numeric(12,2) NOT NULL,
    CONSTRAINT order_items_quantity_check CHECK ((quantity > 0))
);


--
-- Name: order_items_id_seq; Type: SEQUENCE; Schema: app; Owner: -
--

CREATE SEQUENCE app.order_items_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: order_items_id_seq; Type: SEQUENCE OWNED BY; Schema: app; Owner: -
--

ALTER SEQUENCE app.order_items_id_seq OWNED BY app.order_items.id;


--
-- Name: orders; Type: TABLE; Schema: app; Owner: -
--

CREATE TABLE app.orders (
    id bigint NOT NULL,
    user_id integer NOT NULL,
    order_number text NOT NULL,
    total_amount numeric(12,2) NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT orders_total_amount_check CHECK ((total_amount >= (0)::numeric))
);


--
-- Name: order_summary; Type: VIEW; Schema: app; Owner: -
--

CREATE VIEW app.order_summary AS
 SELECT o.id AS order_id,
    o.order_number,
    u.email AS user_email,
    o.total_amount,
    o.created_at
   FROM (app.orders o
     JOIN app.users u ON ((u.id = o.user_id)));


--
-- Name: orders_id_seq; Type: SEQUENCE; Schema: app; Owner: -
--

CREATE SEQUENCE app.orders_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: orders_id_seq; Type: SEQUENCE OWNED BY; Schema: app; Owner: -
--

ALTER SEQUENCE app.orders_id_seq OWNED BY app.orders.id;


--
-- Name: order_items id; Type: DEFAULT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.order_items ALTER COLUMN id SET DEFAULT nextval('app.order_items_id_seq'::regclass);


--
-- Name: orders id; Type: DEFAULT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.orders ALTER COLUMN id SET DEFAULT nextval('app.orders_id_seq'::regclass);


--
-- Name: order_items order_items_pkey; Type: CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.order_items
    ADD CONSTRAINT order_items_pkey PRIMARY KEY (id);


--
-- Name: orders orders_order_number_key; Type: CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.orders
    ADD CONSTRAINT orders_order_number_key UNIQUE (order_number);


--
-- Name: orders orders_pkey; Type: CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (id);


--
-- Name: users users_email_key; Type: CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.users
    ADD CONSTRAINT users_email_key UNIQUE (email);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: orders_created_at_idx; Type: INDEX; Schema: app; Owner: -
--

CREATE INDEX orders_created_at_idx ON app.orders USING btree (created_at);


--
-- Name: orders_user_id_idx; Type: INDEX; Schema: app; Owner: -
--

CREATE INDEX orders_user_id_idx ON app.orders USING btree (user_id);


--
-- Name: users_status_idx; Type: INDEX; Schema: app; Owner: -
--

CREATE INDEX users_status_idx ON app.users USING btree (status);


--
-- Name: users users_normalize_email_trigger; Type: TRIGGER; Schema: app; Owner: -
--

CREATE TRIGGER users_normalize_email_trigger BEFORE INSERT OR UPDATE ON app.users FOR EACH ROW EXECUTE FUNCTION app.set_normalized_email();


--
-- Name: order_items order_items_order_id_fkey; Type: FK CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.order_items
    ADD CONSTRAINT order_items_order_id_fkey FOREIGN KEY (order_id) REFERENCES app.orders(id);


--
-- Name: orders orders_user_id_fkey; Type: FK CONSTRAINT; Schema: app; Owner: -
--

ALTER TABLE ONLY app.orders
    ADD CONSTRAINT orders_user_id_fkey FOREIGN KEY (user_id) REFERENCES app.users(id);


--
-- PostgreSQL database dump complete
--

